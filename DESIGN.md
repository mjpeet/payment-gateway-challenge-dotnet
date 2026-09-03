# Design Decisions and Assumptions

Written for the take-home walkthrough. Covers the reasoning behind the API contract,
the code structure, what's tested and what isn't, and what was deliberately left out
of scope — not just what was built.

## How to run

- **Local development**: `docker compose up bank_simulator` (simulator only), then
  `dotnet run --project src/PaymentGateway.Api`. Swagger at the URL printed by
  `dotnet run` (`/swagger`), health check at `/health`.
- **Full containerized stack**: `docker compose up --build` runs both the simulator and
  the API together. API on `http://localhost:5000` (Swagger enabled there too — see
  the Packaging section below for why). Verified working end-to-end.

## API contract

- `POST /payments` — `cardNumber`, `expiryMonth`, `expiryYear`, `currency`, `amount`
  (integer, minor currency unit), `cvv`. `cardNumber` and `cvv` are **strings**, not
  numbers — they're identifiers, not values used in calculation, and a leading zero
  (possible in either) must survive intact.
- `GET /payments/{id}` — `200` with the payment, or `404`.
- Optional `Idempotency-Key` header on `POST /payments` — see Idempotency below.

### Three outcomes, not two

The brief specifies `Authorized` / `Declined` / `Rejected`. A fourth case exists in
practice and needed its own handling:

| Case | Meaning | HTTP status | Persisted? |
|---|---|---|---|
| **Rejected** | Validation failed; the bank is never called | `400` | No |
| **Authorized** / **Declined** | The bank gave a business answer | `201 Created` | Yes |
| **Bank unavailable** | Bank simulator returned `503`, or the connection failed | `502 Bad Gateway` | No |

The 503 case is deliberately **not** folded into `Rejected` or `Declined` — the request
was valid and did reach the bank, but the bank never gave a business answer, so it isn't
a decline; and validation never failed, so it isn't a rejection. It's also deliberately
not asserted as a specific root cause ("authorization failure") — a 503 from a
dependency is opaque, and claiming to know why would be guessing.

### Validation rules and assumptions

Every rule in the brief's field table is implemented as stated. Two additions beyond
the brief, called out explicitly rather than left implicit:

- **`amount > 0`** — not stated in the brief, added as a sanity rule.
- **Currency whitelist: GBP, USD, EUR** — the brief says "no more than 3," doesn't name
  them; these three were chosen.

## Idempotency

Not required by the brief, added because retried payment requests (a merchant's client
timing out and resending) are a realistic failure mode for a real payment gateway, and
it's one of the more common things asked about in this kind of interview.

- No key: no dedup, behaves as if the feature didn't exist.
- New key: processed normally, result stored against the key.
- Repeated key, same payload: bank is never called again; the original stored response
  is replayed verbatim, same status code.
- Repeated key, different payload: `409 Conflict` — this is key reuse, not a retry.

**Concurrency**: the naive version of this (check-then-save as two separate steps) has
a race — two simultaneous requests with the same brand-new key could both pass the
"not seen before" check and both call the bank, exactly the double-processing this
feature exists to prevent. Given this is a payment gateway, that gap was judged worth
fixing rather than only documenting: `IIdempotencyKeyLock` serializes `ProcessAsync`
calls per key (a keyed `SemaphoreSlim`, not a global lock — unrelated keys still run
fully in parallel). Tested with two calls launched via `Task.WhenAll`, not just
sequential awaits, since a sequential test can't actually exercise the race.

Known limitation, not fixed: the lock dictionary and the idempotency store never evict
entries. Fine for this exercise's in-memory, short-lived scope; a production version
would need eviction/TTL on both.

## Code design

- **Domain model (`Payment`) is separate from the wire DTOs** (`PostPaymentResponse`,
  `GetPaymentResponse`). The scaffold's original `PaymentsRepository` stored
  `PostPaymentResponse` directly — changed so the repository stores `Payment` and DTOs
  are only ever produced at the controller boundary. Justified by the idempotency key
  needing a home that isn't a wire DTO, and because it's a clearer signal of Code
  Design than the scaffold's shortcut was.
- **`GetPaymentResponse` exists but the scaffold's original `GetPaymentAsync` returned
  `PostPaymentResponse` instead** — the dedicated GET response type was dead code.
  Fixed to actually use it.
- **Bank-side decline vs. bank-unavailable are different types**
  (`BankAuthorizationResult { Authorized: false }` vs.
  `AcquiringBankUnavailableException`), so it's structurally impossible to conflate
  "the bank said no" with "we couldn't reach the bank."
- **One type per file, filename matching the type**, applied consistently after an
  initial pass left a few multi-type files (`BankModels.cs`, `IPaymentService.cs`
  bundling five types, `ErrorResponse.cs` bundling two) — split once flagged, for the
  usual reason: you should be able to find a type from its filename.

## Testing

36+ tests across five areas: request validation (including explicit boundary-passing
assertions, not just past-boundary failures), the bank client against a fake HTTP
handler (authorize / decline / 503 / connection failure), the repository (including a
500-way concurrent-write test), the payment service orchestration (fully mocked,
including both idempotency branches and the two concurrency tests above), and the full
HTTP pipeline via `WebApplicationFactory` with a stub bank client.

**Deliberately not automated**:
- End-to-end tests against the real Mountebank simulator — needs `docker compose up`
  running, doesn't fit a plain `dotnet test` run. Exercised manually instead (see
  `DOCKER_TESTING_NOTES.md` for the actual verified round trip).
- Assertions on log content or metric values — brittle (breaks on any message
  reword) and low-signal for a 3-5 hour exercise. The HTTP-observable behavior of the
  observability pieces (health check responds, correlation id is echoed/generated) is
  tested; what actually gets logged isn't.
- Formal consumer-driven contract testing (Pact-style) — exists to protect
  independently-evolving services from breaking each other; there's no second team or
  deployed consumer here for it to protect.

## Observability

Scoped down from a fuller "OpenTelemetry + OTLP export" design, explicitly:

| Kept | Dropped, and why |
|---|---|
| Structured logging, field names only on rejection (never values) | Full OpenTelemetry distributed tracing — earns its cost across multiple service hops you don't fully control; this system has one service and one downstream call |
| Correlation id (client-supplied `X-Correlation-Id`, echoed + logged) | Automatic ASP.NET Core / `HttpClient` OTel instrumentation — feeds a tracing pipeline that doesn't exist here |
| Business metrics via built-in `System.Diagnostics.Metrics` (no external package) | OTLP export — no collector anywhere in this exercise's scope; nothing to point it at, and nothing to demo |
| Liveness-only `/health` (deliberately doesn't ping the bank simulator — "is this service up" is a different question from "is my dependency up") | |

The full version is the right answer once there's a real deployment with a collector
and more than one service to correlate across — noted here as the acknowledged next
step, not overlooked.

## Packaging and hosting

Multi-stage Docker build (SDK build stage, ASP.NET runtime stage), non-root (`USER app`
— .NET 8's images ship this user but it's opt-in), Kestrel unchanged, Docker Compose for
local orchestration with service-name-based networking (`http://bank_simulator:8080`,
not `localhost`, since container-to-container traffic can't resolve that), all
config through environment variables. No Kubernetes manifests, no CI/CD pipeline, no
cloud-provider specifics — out of scope for what this is.

`ASPNETCORE_ENVIRONMENT=Development` is set in the container (enabling Swagger there
too). This is not what a real production container would do — a genuine production
image would default to `Production` and keep Swagger off. It's set here as a deliberate,
documented choice specific to this being a reviewable take-home exercise, not an
oversight.

### Real bugs found by actually running this

Three bugs here were only found by executing the code, not by review — worth stating
plainly since it's the strongest evidence the testing/packaging claims above aren't just
theoretical. Full detail in `DOCKER_TESTING_NOTES.md`.

1. **`CreatedAtAction(nameof(GetPaymentAsync), ...)` never worked.** ASP.NET Core
   strips the `Async` suffix from action names by default when generating routes, so
   the registered route name is `"GetPayment"`. `nameof(GetPaymentAsync)` compiles fine
   but doesn't match any route, so every successful `POST /payments` threw
   `InvalidOperationException: No route matches the supplied values` — a 500 on the
   happy path, undetectable by reading the code, only found by actually calling the
   endpoint. Fixed by using the literal string `"GetPayment"`, with the failure mode
   documented inline so a future rename doesn't silently reintroduce it.
2. **Missing `.dockerignore`** let a Windows-built local `bin/`/`obj/` leak into the
   Docker build context, clobbering the container's freshly-restored
   `obj/project.assets.json` with one referencing a Visual-Studio-only NuGet path,
   breaking `dotnet publish` inside the Linux build stage every time.
3. **Invalid UTF-8 in `docker-compose.yml`** — a Windows-1252 em-dash byte in a comment
   broke YAML parsing entirely. Fixed by re-encoding, and by replacing em dashes in that
   file with plain ASCII going forward, since a YAML parse failure is a much harder
   failure than a C# comment merely rendering oddly.
