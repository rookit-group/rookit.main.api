---
applyTo: "MainHub.Api/Authorization/**,MainHub.Api/Endpoints/**,MainHub.Api/Services/**,MainHub.Api/Repositories/**"
---

# Per-garage RBAC & authorization

This is the authorization model for **internal (company-staff) users**. It is a
per-garage, scope-based RBAC built on a two-stage JWT flow. Read this before
touching anything under `Authorization/`, garage-scoped endpoints, or the
services/repositories that back them.

## Core mental model

- **Scopes are code. Roles are data.**
  - The permission vocabulary is a fixed set of constants in
    [Scope.cs](../../MainHub.Api/Authorization/Scope.cs)
    (`garage:read/manage`, `role:read/manage`, `staff:read/manage`, plus the
    `*` wildcard). Application code is written **only** against scopes.
  - A **role** is a garage-defined bundle of scopes stored in the `roles`
    table. Owners create/edit roles in the UI. **Never branch on a role name**
    in code — role names are owner-defined and meaningless to the system.
  - Adding a capability = add a `Scope` constant + gate the endpoint with it.
    Owners can then assign it to their roles. Never invent ad-hoc permission
    strings outside `Scope`.

- **Two-stage token flow** (both are separate JWT schemes with their own signing
  key + lifetime; see [Program.cs](../../MainHub.Api/Program.cs) `AddJwtBearer`):
  1. **`InternalIdentityJwt`** (stage 1, ~60 min) — proves *identity only*. Minted
     at login. Carries no garage and no scopes. Used to list garages and open a
     garage session.
  2. **`GarageJwt`** (stage 2, ~5 min) — bound to *one* garage. Embeds the
     `garage_id` claim and a space-delimited `scope` claim. Minted when the user
     opens a garage session. **This is the only token garage-scoped endpoints
     accept.**

- **Scopes are resolved from the DB exactly once, at mint time**
  (`PermissionService.ResolveScopesAsync` → `GetMemberScopesAsync`), then baked
  into the `GarageJwt`. Request-time authorization is therefore a **pure claim
  check with zero DB access**. The short 5-min lifetime bounds staleness after a
  role change. Do **not** add per-request membership lookups for authorization.

## How to gate a garage-scoped endpoint

Always use the `RequireScope` helper — never hand-write the policy string:

```csharp
group
    .MapGet("", ListThingsAsync)
    .RequireScope(Scope.StaffRead);        // read
group
    .MapPost("", CreateThingAsync)
    .AddEndpointFilter<ValidationFilter<CreateThingDto>>()
    .RequireScope(Scope.StaffManage);      // write
```

- `RequireScope(scope)` →
  [ScopeEndpointExtensions](../../MainHub.Api/Endpoints/ScopeEndpointExtensions.cs)
  → resolves to the convention policy `scope:{scope}` produced on-demand by
  [ScopePolicyProvider](../../MainHub.Api/Authorization/ScopePolicyProvider.cs).
  No per-scope policy needs registering.
- Every scope policy pins the `GarageJwt` scheme (unauthenticated → 401,
  authenticated-but-unscoped → 403) and adds a `ScopeRequirement`.
- [ScopeAuthorizationHandler](../../MainHub.Api/Authorization/ScopeAuthorizationHandler.cs)
  does two checks, both claim-only:
  1. route `{garageId}` **must equal** the token's `garage_id` claim (blocks
     using garage A's token on garage B), and
  2. the token's scopes must `Scope.Grants(...)` the required scope (wildcard
     counts).
- The garage-context strings (`RouteKey="garageId"`, `GarageIdClaim="garage_id"`,
  `ScopeClaim="scope"`) live in one place —
  [GarageContext.cs](../../MainHub.Api/Authorization/GarageContext.cs). Reuse
  them; never re-type the literals.

> ⚠️ The handler only verifies `garageId` context — it does **not** verify that a
> `{roleId}`/`{userId}` in the route belongs to that garage. **Services must
> re-check ownership** (e.g. `RoleService`/`MembershipService` resolve the role
> and assert `role.GarageId == garageId`).

## Where the rules live: endpoints vs services

- **Endpoints** are thin: translate DTO ↔ service call, read the actor's scopes
  via `tokenService.GetScopesFromClaims(user)`, and map exceptions to status
  codes. **No business rules in endpoints.**
- **Input-shape validation** goes in FluentValidation validators under
  `MainHub.Api/Validators` (applied with `.AddEndpointFilter<ValidationFilter<TDto>>()`),
  **not** in services. `.NotEmpty()` on a `Guid` rejects `Guid.Empty`.
- **Domain/authorization rules** live in the **service** layer and are
  unbypassable (see guards below).

### The escalation guard (critical)

You can only grant scopes you yourself hold. Enforced in one place —
[PermissionGuard.EnsureCanGrant](../../MainHub.Api/Authorization/PermissionGuard.cs)
— and called wherever scopes are handed out (`RoleService` when defining a
role's scopes, `MembershipService` when assigning a role). Because the wildcard
grants everything, **only an owner (wildcard holder) can grant the Owner role**.
Violation → `UnauthorizedAccessException` → **403**.

### Other invariants enforced in services

- **System-role immutability:** the seeded per-garage **"Owner"** role has
  `is_system = true` and holds `{'*'}`. Services refuse to edit/delete system
  roles so a garage can never strip its own admin access. Violation → **409**.
- **Last-staff-manager guard:** a garage must always keep ≥1 member with
  `staff:manage`. Removing/demoting the last one → **409**
  (`MembershipService.EnsureNotLastStaffManagerAsync`).
- **Invitee must already exist:** you can only invite a user who has an
  `internal_user_profiles` row (created at their first login). Inviting an
  unknown user → **404** with a user-facing message ("No such user. The user
  must sign in at least once before they can be invited."). We **never**
  create the invitee's profile on invite.
- **Duplicate-member guard:** inviting an existing member → **409**.
- **Cross-garage role isolation:** assigning a role from another garage → **409**
  (also enforced at the DB level by the composite FK, see below).

## Endpoint error-mapping convention

Handlers `try/catch` service exceptions and map:

| Exception                       | Status | Meaning                                            |
| ------------------------------- | ------ | -------------------------------------------------- |
| `KeyNotFoundException`          | 404    | not a member / user or role doesn't exist          |
| `UnauthorizedAccessException`   | 403    | escalation (grant scope you don't hold)            |
| `InvalidOperationException`     | 409    | conflict: system role, duplicate, last-admin, in-use, cross-garage |

- **Staff endpoints** use `Results.Problem(ex.Message, statusCode: X)` for **all**
  mapped errors so the message reaches the UI (`Results.Forbid()` has no body).
- **Role endpoints** use bare `Results.NotFound()` (no body) to avoid leaking
  existence.

## Self-serve garage creation

`POST /api/garages` runs under **`InternalIdentityJwt`** with **no `RequireScope`**
(chicken-and-egg: there's no garage yet). It `EnsureAsync`-es the caller's *own*
profile (idempotent) and calls `GarageService.CreateAsync`, which atomically
creates the garage + a wildcard `Owner` system-role + the owner membership.
Contrast with invite, which requires the *other* user to already exist.

## Database invariants (defence in depth)

See [001_initial.sql](../../MainHub.Api/Migrations/001_initial.sql):

- `internal_user_profiles.user_id` is **UNIQUE** → makes get-or-create on login
  race-safe (`INSERT ... ON CONFLICT (user_id) DO NOTHING`).
- `roles` has `UNIQUE (garage_id, name)` and `UNIQUE (id, garage_id)`; the latter
  is the composite candidate key the membership table FKs against.
- `internal_user_profiles_garages` has a **composite FK `(role_id, garage_id)` →
  `roles(id, garage_id)`** so a membership's role must belong to the same garage.
  `ON DELETE NO ACTION` = "a role can't be deleted while a member holds it"
  (behind the service check), while a **garage** delete still cascades cleanly.
- `role_id` is `NOT NULL` → every member always has exactly one role; there is no
  scope-less "zombie" member state, so permission checks never need a "no role"
  branch.

## Testing (required)

- Every entity/query/service rule is covered by **integration tests** against a
  real Postgres (Testcontainers `postgres:16-alpine`; **Docker Desktop must be
  running**). `[Collection("Postgres")]`, reset per test.
- Handlers are tested by calling the `internal static` methods **directly** with a
  hand-built `ClaimsPrincipal` (there is no HTTP harness). Build the actor's
  scopes with a `PrincipalWithScopes(params string[])` helper that writes the
  `GarageContext.ScopeClaim` (space-joined); `Owner = PrincipalWithScopes(Scope.Wildcard)`.
- Assert on the `IResult` runtime type: `Ok<T>`, `Created<T>`, `NoContent`,
  `NotFound`, `ProblemHttpResult` (`.StatusCode`).
- **No speculative code.** Add a method/query only when a real consumer + test
  exists for it.
- Commands (Windows PowerShell, cwd `D:\rookit.main.api`; fresh process each call):
  - `dotnet build "MainHub.Api\MainHub.Api.csproj" -clp:ErrorsOnly`
  - `dotnet test "MainHub.Api.IntegrationTests\MainHub.Api.IntegrationTests.csproj" -clp:"ErrorsOnly;Summary"`

## Adding a new garage-scoped capability — checklist

1. Add a `Scope` constant (+ to `Scope.All` if it's a concrete, assignable scope).
2. Add the endpoint in an `*Endpoints.cs` group under
   `/api/garages/{garageId}/...`, gate it with `.RequireScope(Scope.X)`.
3. Put input-shape checks in a validator; put domain rules + the escalation guard
   in the service.
4. Map service exceptions to 404/403/409 per the table above.
5. DTOs go in `Shared.Contracts/DTOs/**` (see the shared-contracts instructions).
6. Cover repo query, service rules, endpoint mapping, and validator with tests.
