# PALM BAY difficulty benchmark

Result: **PASS** · generated 2026-10-06 10:09 · real `AirportGame.BotInput`, fixed step 0.05s, neutral bot memory.

## 1. Single-task bot time (first flight, from aircraft parked)

| Task | Done | Seconds | Walk s / m | Drive s / m | Stationary s | Note |
|---|---|---:|---:|---:|---:|---|
| Meals | yes | 10.9 | 0.7 / 4.0 | 2.3 / 8.2 | 7.9 |  |
| Baggage | yes | 12.7 | 0.4 / 2.7 | 9.3 / 31.5 | 3.0 |  |
| Fuel | yes | 16.7 | 2.9 / 13.2 | 4.2 / 14.2 | 9.6 |  |
| Boarding | yes | 13.2 | 0.1 / 0.5 | 0.0 / 0.0 | 13.1 | Meals+Fuel pre-completed via domain API so the gate can open |

Taxi-in before the stand opens: 7.3s. Service window per flight: 75s from arrival.

<details><summary>Meals timeline</summary>

- 0.1s — on foot | meal=Idle cargo=empty
- 0.7s — driving Meals | meal=Idle cargo=empty
- 1.8s — driving Meals | meal=Ordered cargo=empty
- 6.8s — driving Meals | meal=Ready cargo=empty
- 7.1s — driving Meals | meal=Idle cargo=loaded
- 8.4s — driving Meals | meal=Idle cargo=loaded delivering
- 10.9s — driving Meals | meal=Idle cargo=empty

</details>

<details><summary>Baggage timeline</summary>

- 0.1s — on foot | arrivalBagsReturned=False cargo=empty
- 0.4s — driving Baggage | arrivalBagsReturned=False cargo=empty
- 3.4s — driving Baggage | arrivalBagsReturned=False cargo=arrival bags
- 6.5s — driving Baggage | arrivalBagsReturned=True cargo=empty
- 6.9s — driving Baggage | arrivalBagsReturned=True cargo=loaded
- 10.2s — driving Baggage | arrivalBagsReturned=True cargo=loaded delivering
- 12.7s — driving Baggage | arrivalBagsReturned=True cargo=empty

</details>

<details><summary>Fuel timeline</summary>

- 0.1s — on foot | nozzle=AtStation hose=OnTruck valve=closed tank=empty
- 1.1s — on foot | nozzle=Held hose=OnTruck valve=closed tank=empty
- 1.6s — on foot | nozzle=OnTruck hose=OnTruck valve=closed tank=empty
- 2.0s — on foot | nozzle=OnTruck hose=OnTruck valve=open tank=empty
- 2.0s — on foot | nozzle=OnTruck hose=OnTruck valve=open tank=filling
- 8.0s — on foot | nozzle=OnTruck hose=OnTruck valve=open tank=full
- 8.0s — on foot | nozzle=AtStation hose=OnTruck valve=closed tank=full
- 8.6s — driving Fuel | nozzle=AtStation hose=OnTruck valve=closed tank=full
- 12.8s — on foot | nozzle=AtStation hose=OnTruck valve=closed tank=full
- 13.2s — on foot | nozzle=AtStation hose=Held valve=closed tank=full
- 13.6s — on foot | nozzle=AtStation hose=OnAircraft valve=closed tank=full
- 13.7s — on foot | nozzle=AtStation hose=OnAircraft valve=closed tank=filling
- 16.6s — on foot | nozzle=AtStation hose=OnAircraft valve=closed tank=empty

</details>

<details><summary>Boarding timeline</summary>

- 0.1s — on foot | gate=closed boarded=0/8
- 0.2s — on foot | gate=open boarded=0/8
- 8.7s — on foot | gate=open boarded=1/8
- 9.3s — on foot | gate=open boarded=2/8
- 10.0s — on foot | gate=open boarded=3/8
- 10.6s — on foot | gate=open boarded=4/8
- 11.3s — on foot | gate=open boarded=5/8
- 11.9s — on foot | gate=open boarded=6/8
- 12.6s — on foot | gate=open boarded=7/8
- 13.2s — on foot | gate=closed boarded=8/8

</details>

## 2. Difficulty budget (derived from section 1)

| Measure | Value |
|---|---:|
| Bot work per flight (sum of four single tasks, no travel between them) | 53.4s |
| Usable window per flight (75s − taxi-in) | 67.7s |
| One worker's window utilisation | 79% |
| Arrival gaps | 35 / 50 / 55 / 55s (mean 48.8s) |
| Workers needed to keep pace (work ÷ mean gap) | 1.10 |
| Flights in a 300s shift / needed for 3★ | 5 / 4 |

## 3. Full 300s shift

### 1 bot (partner idle)

Score **250** · 0★ · departed 0 / missed 5 · tasks 5/20 · failed tasks 0 · spills 0

| Flight | Arrive | Stand ready | Deadline | Meals | Baggage | Fuel | Boarding | Result | Slack s |
|---|---:|---:|---:|---:|---:|---:|---:|---|---:|
| UBA826 | 0.0 | 7.3 | 75.0 | +3.3 | — | — | — | Missed | — |
| QMR152 | 35.0 | 44.1 | 110.0 | — | +35.8 | — | — | Missed | — |
| AZU407 | 85.0 | 92.3 | 160.0 | +26.8 | — | — | — | Missed | — |
| GLO219 | 140.0 | 149.1 | 215.0 | — | +15.3 | — | — | Missed | — |
| TAM638 | 195.0 | 202.3 | 270.0 | +21.8 | — | — | — | Missed | — |

Task cells = seconds after the stand opened. Bot time split:

- seat 1: walking 7.2s, driving 10.6s, stationary 282.2s (cart held: meals 106.2s, baggage 186.6s, fuel 0.0s) · **stalls ≥10s: 5 (263.4s total, longest 74.5s)**

### 2 bots

Score **50** · 0★ · departed 0 / missed 5 · tasks 1/20 · failed tasks 0 · spills 0

| Flight | Arrive | Stand ready | Deadline | Meals | Baggage | Fuel | Boarding | Result | Slack s |
|---|---:|---:|---:|---:|---:|---:|---:|---|---:|
| UBA826 | 0.0 | 7.3 | 75.0 | +5.5 | — | — | — | Missed | — |
| QMR152 | 35.0 | 44.1 | 110.0 | — | — | — | — | Missed | — |
| AZU407 | 85.0 | 92.3 | 160.0 | — | — | — | — | Missed | — |
| GLO219 | 140.0 | 149.1 | 215.0 | — | — | — | — | Missed | — |
| TAM638 | 195.0 | 202.3 | 270.0 | — | — | — | — | Missed | — |

Task cells = seconds after the stand opened. Bot time split:

- seat 0: walking 1.1s, driving 6.0s, stationary 293.0s (cart held: meals 0.0s, baggage 299.0s, fuel 0.0s) · **stalls ≥10s: 2 (288.6s total, longest 224.3s)**
- seat 1: walking 4.4s, driving 3.3s, stationary 292.4s (cart held: meals 12.3s, baggage 0.0s, fuel 247.5s) · **stalls ≥10s: 2 (276.7s total, longest 224.2s)**
