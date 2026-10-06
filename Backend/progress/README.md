# M6 account progress

The Unity adapter uses the existing Firebase Auth project ID (`palmairport` in
`Assets/Resources/palmbay-auth.json`) and its Firebase ID token. It calls Firestore
REST directly; no extra SDK, administrator key, Cloud Function, or paid dependency
is required by this implementation. This change does not deploy backend rules or
assert that the default Firestore database is already enabled.

Records live at `accountProgress/{authenticatedUid}/settlements/{receiptId}`.
`receiptId` is lowercase SHA-256 of `matchId + "\n" + roundId`. Each record is
created once with `POST .../settlements?documentId={receiptId}`. If a response is
lost after the server commits, the retry gets HTTP 409; the client reads that
same document and compares the entire immutable result before acknowledging it.
If the rules reject a duplicate create with 403 before evaluating the existence
precondition, an owner-scoped read performs the same comparison. A 403 whose
document cannot be read as an identical receipt remains a permissions failure.
Changed results under the same identity stay pending with `receipt_conflict`.
No aggregate counter is written. A paginated account-scoped read derives totals
and the best stars for each level from distinct receipts.

## Backend setup (manual deployment; not performed by the agent)

1. Check that project `palmairport` has a Firestore **Native mode**, `(default)`
   database. Choose its location through the project's normal setup process.
2. Review existing Firestore rules before deploying. The supplied file is a
   complete deny-by-default ruleset for this subsystem. If other features already
   store Firestore data, merge the `accountProgress` match into their rules;
   replacing a complete ruleset would deny their unrelated data. Broad existing
   allow rules must not also match `accountProgress` because matching allow rules
   combine with OR.
3. From this directory, an authorized operator can deploy the reviewed rules with
   `firebase deploy --only firestore:rules --project palmairport`.
4. Sign in with two real test accounts. Each should load only its own receipts;
   cross-account reads/creates, signed-out writes, updates and deletes must fail.
   Retry the same completed match after temporarily disabling network access and
   verify that there is exactly one receipt. Also verify 0–3 stars, integer bounds,
   and rejection of missing/extra fields. No composite index is needed for these
   direct collection lists.

The owner identity is verified on the server by Firebase Auth and Security Rules.
These rules enforce owner scope, immutable storage and bounded schema. Gameplay
and participation are validated by the existing authoritative peer session plus
the local settlement capture. They are **not server-certified anti-cheat**:
a modified client can invent different match IDs/results within its own account.
Such certification would need a trusted match service and is outside M6.

## Runtime contract

Own `AccountProgressRuntime` on the persistent app state and call `Pump(dt)` after
the auth service. Capture `SettlementIdentity.CaptureIdentity(user, isFakeAuth)`
once at round start. At settlement, call `RecordSettlement` with that captured
identity and the authoritative match/round/result. Pass `eligibleAtSettlement`
only for a player still participating as a human in that same round. Each device
writes its own account only. Guest/fake identities return null; an account change
between capture and settlement is rejected. Do not capture identity again after
host migration or when logging in on the result screen.

`RecordSettlement == true` means the immutable result was accepted for sync.
Normally it is saved immediately to `palmbay-progress-outbox.json` under Unity's
persistent data directory. If storage fails, `storage_error` remains visible and
the in-memory result retries storage before any network request. Quitting before
storage succeeds cannot preserve that result. A corrupt or unreadable existing
outbox is not overwritten. Writes use a flushed temporary file and atomic replace.
Tokens and display names are never stored in that file.
Local persistence retries at most once every five seconds after a failure,
independently of login or backend configuration. Signing out does not stop an
already captured result from recovering its durable save; it still cannot upload
until that original account signs back in.

Pending accounts coexist in the outbox, but only the currently signed-in real UID
is sent. Sign-out/account switch cancels in-flight HTTP and clears displayed cloud
progress. A response lost during cancellation is safe to retry on that original
account. A 401 waits for a changed ID token; the existing Firebase auth adapter
owns token renewal. Network errors retry with bounded exponential backoff (2–60
seconds); 403 retries after at least 60 seconds and reports `permission_denied`.
404 reports `backend_missing`; empty/invalid project IDs report `unconfigured`.
Retries continue across scenes and restarts. `RequestLoad()` reloads cloud totals;
new sign-in triggers a load automatically. A partial or failed paginated load
does not replace the last complete summary.
Acknowledging a newly created receipt before the first account history load
keeps the status `pending`/`loading`. The complete account summary is published
only after all initial pages arrive. `synced` additionally requires no pending
receipts for that account.

## Validation

`sh Tools/test-progress.sh` exercises capture/eligibility, durable retry,
account isolation, lost-response duplicates, conflicting receipts, 401 token
renewal, permission/network errors, corrupted storage and paginated load against
deterministic transport/storage fakes. It performs no external write or login.
It does not establish that production rules are deployed or that live credentials
have Firestore access. Backend rules should additionally be exercised with the
Firestore emulator before production deployment.

### Local rules acceptance

`test_rules.py` uses Python 3's standard library, unsigned emulator-only mock
Firebase ID tokens, and the emulator REST API to load this directory's actual
`firestore.rules`. It checks owner/cross-account/signed-out create, get and list;
all update/delete denials; duplicate and conflicting creates; schema fields,
types and numeric bounds; and default denial outside this collection. Accepted
boundary receipts and final list counts verify denied writes did not add records.
Every run uses fresh account IDs. It leaves test data in the disposable emulator.

With Firebase CLI, Java and the Firestore emulator already installed/cached,
start a disposable emulator from this directory:

```sh
firebase emulators:start --only firestore --project demo-palmbay-progress --config firebase.json
```

Then from the game root in another terminal:

```sh
sh Tools/test-progress-rules.sh
```

Alternatively, run `python3 Backend/progress/test_rules.py`. An existing local
emulator can be selected using `--host 127.0.0.1:8180` (or
`FIRESTORE_EMULATOR_HOST`) and `--project demo-palmbay-progress`. The harness
rejects non-loopback addresses, redirects and project IDs without the `demo-`
prefix; it ignores proxy settings and never uses saved credentials. Loading rules
replaces rules only for that demo project in the emulator. The Python harness
does not install dependencies or download anything; Firebase CLI may download
an emulator if it is not already cached.

The harness has been syntax checked. Emulator execution is still pending because
Firebase CLI/the Firestore emulator are unavailable locally. Neither this harness
nor its eventual emulator result establishes production deployment or real login
access.

Official API references used to verify the wire contract:

- [Firestore REST authentication and rules](https://firebase.google.com/docs/firestore/use-rest-api)
- [createDocument and documentId](https://firebase.google.com/docs/firestore/reference/rest/v1/projects.databases.documents/createDocument)
- [Collection list and pagination](https://firebase.google.com/docs/firestore/reference/rest/v1/projects.databases.documents/list)
