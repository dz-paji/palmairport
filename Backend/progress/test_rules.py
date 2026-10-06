#!/usr/bin/env python3
"""Exercise firestore.rules with stdlib REST against a local demo emulator only."""

import argparse
import base64
import hashlib
import ipaddress
import json
import os
from pathlib import Path
import re
import sys
import time
from urllib import error, parse, request
import uuid


class NoRedirect(request.HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):
        raise RuntimeError("Emulator redirects are forbidden.")


def emulator_origin(host):
    parsed = parse.urlsplit("http://" + host)
    if (parsed.username or parsed.password or parsed.path or parsed.query or
            parsed.fragment or not parsed.hostname or parsed.port is None):
        raise ValueError("Use a loopback host:port, such as 127.0.0.1:8180.")
    name = parsed.hostname
    if name == "localhost":
        name = "127.0.0.1"
    if not ipaddress.ip_address(name).is_loopback:
        raise ValueError("Only a literal loopback address or localhost is allowed.")
    if not 1 <= parsed.port <= 65535:
        raise ValueError("Invalid emulator port.")
    authority = "[{}]".format(name) if ":" in name else name
    return "http://{}:{}".format(authority, parsed.port)


def mock_token(project, uid):
    def encode(value):
        return base64.urlsafe_b64encode(
            json.dumps(value, separators=(",", ":")).encode()).decode().rstrip("=")

    now = int(time.time())
    claims = {"aud": project, "iss": "https://securetoken.google.com/" + project,
              "sub": uid, "user_id": uid, "iat": now, "exp": now + 3600,
              "auth_time": now, "firebase": {"sign_in_provider": "google.com",
                                            "identities": {}}}
    return encode({"alg": "none", "typ": "JWT"}) + "." + encode(claims) + "."


def wire_fields(values):
    fields = {}
    for key, value in values.items():
        if isinstance(value, bool):
            fields[key] = {"booleanValue": value}
        elif isinstance(value, int):
            fields[key] = {"integerValue": str(value)}
        elif isinstance(value, float):
            fields[key] = {"doubleValue": value}
        elif isinstance(value, str):
            fields[key] = {"stringValue": value}
        else:
            raise TypeError("Unsupported test field value.")
    return {"fields": fields}


class Acceptance:
    def __init__(self, origin, project):
        self.origin, self.project = origin, project
        # Ignore system proxies; even localhost requests must remain local.
        self.http = request.build_opener(request.ProxyHandler({}), NoRedirect())
        self.count = 0
        self.prefix = "/v1/projects/{}/databases/(default)/documents".format(project)
        run = uuid.uuid4().hex
        self.owner, self.other = "rules-owner-" + run, "rules-other-" + run

    def call(self, method, path, uid=None, body=None):
        headers = {"Content-Type": "application/json"}
        if uid is not None:
            headers["Authorization"] = "Bearer " + mock_token(self.project, uid)
        data = json.dumps(body).encode() if body is not None else None
        req = request.Request(self.origin + path, data=data, headers=headers, method=method)
        try:
            with self.http.open(req, timeout=10) as response:
                status, raw = response.status, response.read()
        except error.HTTPError as response:
            status, raw = response.code, response.read()
        try:
            decoded = json.loads(raw) if raw else {}
        except (ValueError, UnicodeDecodeError):
            raise AssertionError("Emulator returned a non-JSON response (HTTP {}).".format(status))
        return status, decoded

    def expect(self, label, method, path, uid, body=None, statuses=(200,)):
        status, decoded = self.call(method, path, uid, body)
        if status not in statuses:
            detail = decoded.get("error", {}).get("status", "unexpected response")
            raise AssertionError("{}: expected {}, got {} ({})".format(
                label, statuses, status, detail))
        self.count += 1
        print("PASS  " + label)
        return decoded

    def collection(self, uid):
        return self.prefix + "/accountProgress/" + uid + "/settlements"

    def receipt(self, label, uid=None):
        match = "rules-test-" + label + "-" + uuid.uuid4().hex
        receipt_id = hashlib.sha256((match + "\n1").encode()).hexdigest()
        return {"schemaVersion": 1, "uid": uid or self.owner, "receiptId": receipt_id,
                "matchId": match, "roundId": 1, "levelId": "coral-bay-1", "stars": 2,
                "score": 400, "completedFlights": 2, "completedTasks": 8,
                "elapsedSeconds": 300}

    def create(self, label, values, actor, target=None, statuses=(200,)):
        path = self.collection(target or self.owner) + "?documentId=" + values["receiptId"]
        return self.expect(label, "POST", path, actor, wire_fields(values), statuses)

    def run(self):
        rules = Path(__file__).with_name("firestore.rules").read_text(encoding="utf-8")
        path = "/emulator/v1/projects/{}:securityRules".format(self.project)
        self.expect("load checked-in firestore.rules", "PUT", path, None,
                    {"rules": {"files": [{"name": "firestore.rules", "content": rules}]}})

        # A denied request first catches accidental admin/bypass authentication.
        self.expect("unauthenticated list denied", "GET", self.collection(self.owner),
                    None, statuses=(403,))
        row = self.receipt("owner")
        self.create("owner creates receipt", row, self.owner)
        doc = self.collection(self.owner) + "/" + row["receiptId"]
        loaded = self.expect("owner reads receipt", "GET", doc, self.owner)
        if loaded.get("fields") != wire_fields(row)["fields"]:
            raise AssertionError("Owner read did not preserve the immutable receipt fields.")
        self.expect("cross-account get denied", "GET", doc, self.other, statuses=(403,))
        self.expect("unauthenticated get denied", "GET", doc, None, statuses=(403,))
        self.expect("cross-account list denied", "GET", self.collection(self.owner),
                    self.other, statuses=(403,))
        self.create("cross-account create denied", self.receipt("cross"), self.other,
                    statuses=(403,))
        self.create("unauthenticated create denied", self.receipt("guest"), None,
                    statuses=(403,))
        self.create("payload UID mismatch denied", self.receipt("uid", self.other),
                    self.owner, statuses=(403,))

        changed = dict(row, score=401)
        for actor, name in [(self.owner, "owner"), (self.other, "cross-account"), (None, "unauthenticated")]:
            self.expect(name + " update denied", "PATCH", doc, actor,
                        wire_fields(changed), statuses=(403,))
            self.expect(name + " delete denied", "DELETE", doc, actor, statuses=(403,))
        self.create("duplicate create cannot add a receipt", row, self.owner,
                    statuses=(403, 409))
        self.create("conflicting duplicate cannot overwrite receipt", changed, self.owner,
                    statuses=(403, 409))
        unchanged = self.expect("original fields survive denied writes", "GET", doc, self.owner)
        if unchanged.get("fields") != loaded.get("fields"):
            raise AssertionError("A denied write changed the receipt.")

        bounds = {"roundId": (0, 1000000000), "stars": (0, 3), "score": (0, 100000000),
                  "completedFlights": (0, 10000), "completedTasks": (0, 100000),
                  "elapsedSeconds": (1, 86400)}
        for field, (lower, upper) in bounds.items():
            for value in [lower - 1, upper + 1, 1.5, "1", True]:
                invalid = self.receipt("invalid")
                invalid[field] = value
                self.create("{} rejects {!r}".format(field, value), invalid, self.owner,
                            statuses=(403,))
        for label, key, value in [
                ("wrong schema", "schemaVersion", 2),
                ("noninteger schema", "schemaVersion", "1"),
                ("empty match", "matchId", ""),
                ("oversized match", "matchId", "m" * 201),
                ("nonstring match", "matchId", 1),
                ("empty level", "levelId", ""),
                ("oversized level", "levelId", "a" * 65),
                ("invalid level characters", "levelId", "a/b"),
                ("nonstring level", "levelId", 1),
                ("invalid receipt ID", "receiptId", "x" * 64),
                ("uppercase receipt ID", "receiptId", "A" * 64)]:
            invalid = self.receipt(label)
            invalid[key] = value
            self.create(label + " denied", invalid, self.owner, statuses=(403,))
        for field in row:
            invalid = self.receipt("missing")
            del invalid[field]
            # Use a valid path ID even when the receiptId field is absent.
            document_id = invalid.get("receiptId", hashlib.sha256(uuid.uuid4().bytes).hexdigest())
            self.expect("missing " + field + " denied", "POST",
                        self.collection(self.owner) + "?documentId=" + document_id,
                        self.owner, wire_fields(invalid), statuses=(403,))
        invalid = self.receipt("extra")
        invalid["extra"] = "forbidden"
        self.create("extra field denied", invalid, self.owner, statuses=(403,))
        mismatch = self.receipt("path-mismatch")
        self.expect("receipt ID/path mismatch denied", "POST",
                    self.collection(self.owner) + "?documentId=" + "0" * 64,
                    self.owner, wire_fields(mismatch), statuses=(403,))

        # Endpoint values prove bounds are inclusive and owner B has its own collection.
        for name, endpoint in [("lower", 0), ("upper", 1)]:
            valid = self.receipt(name)
            for field, limits in bounds.items():
                valid[field] = limits[endpoint]
            # Keep identity deterministic after selecting a round boundary.
            valid["receiptId"] = hashlib.sha256(
                (valid["matchId"] + "\n" + str(valid["roundId"])).encode()).hexdigest()
            self.create(name + " inclusive bounds accepted", valid, self.owner)
        other = self.receipt("other", self.other)
        self.create("other owner creates own receipt", other, self.other, target=self.other)
        for uid, expected in [(self.owner, 3), (self.other, 1)]:
            listed = self.expect("owner lists only own receipts ({} records)".format(expected),
                                 "GET", self.collection(uid), uid)
            documents = listed.get("documents", [])
            if len(documents) != expected or any(
                    item["fields"]["uid"] != {"stringValue": uid} for item in documents):
                raise AssertionError("List count/account isolation failed.")
        self.expect("unrelated collection denied", "GET", self.prefix + "/unrelated",
                    self.owner, statuses=(403,))
        print("Firestore emulator rules acceptance passed: {} checks.".format(self.count))
        print("Only local demo data was used; production deployment remains unverified.")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--host", default=os.environ.get("FIRESTORE_EMULATOR_HOST", "127.0.0.1:8180"))
    parser.add_argument("--project", default="demo-palmbay-progress")
    args = parser.parse_args()
    try:
        if not re.fullmatch(r"demo-[a-z0-9-]+", args.project):
            raise ValueError("Only demo-* project IDs are allowed.")
        origin = emulator_origin(args.host)
        Acceptance(origin, args.project).run()
        return 0
    except (ValueError, AssertionError, RuntimeError, OSError, error.URLError) as exc:
        print("FAIL: {}".format(exc), file=sys.stderr)
        print("Start the local Firestore emulator before running this harness. "
              "No dependency or emulator downloads are performed by this script.", file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
