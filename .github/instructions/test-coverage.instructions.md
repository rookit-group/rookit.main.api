---
applyTo: "MainHub.Api/Models/**,MainHub.Api/Repositories/**"
---

# Test coverage for entities & repositories — hard rule

Every time you **create or edit** an entity (`MainHub.Api/Models/**`) or a
repository (`MainHub.Api/Repositories/**`), you **must** create or update the
related tests in the same change so that **all new/changed code is covered by
tests**. A change that touches these files is not complete until its tests
exist, are updated, and pass.

This is non-negotiable: do not leave new or modified entity/repository code
without corresponding test coverage.

## Where tests live

- Integration tests live in `MainHub.Api.IntegrationTests`.
- Each repository has a matching test class:
  `MainHub.Api/Repositories/<Name>Repository.cs` →
  `MainHub.Api.IntegrationTests/<Name>RepositoryTests.cs`.
- Shared test data builders live in
  [Factories.cs](../../MainHub.Api.IntegrationTests/Factories.cs). Add a factory
  helper here when you add a new entity, and update the existing helper when you
  add/change fields on an entity.
- Tests share a database via
  [PostgresFixture.cs](../../MainHub.Api.IntegrationTests/PostgresFixture.cs)
  and are grouped with `[Collection("Postgres")]`.

## When you EDIT an entity (`MainHub.Api/Models/**`)

1. Update the entity's factory helper in `Factories.cs` so every required field
   has a sensible default and any new field is settable.
2. Update the affected repository tests so the new/changed fields are inserted,
   read back, and asserted (including enum/nullable round-tripping).
3. Add tests for any new behavior the field introduces (constraints, defaults,
   optionality).

## When you CREATE an entity (`MainHub.Api/Models/**`)

1. Add a factory helper for it in `Factories.cs`.
2. If it gets a repository, create `<Name>RepositoryTests.cs` (see next section).
3. Cover create/read/update/delete and any relationships (FKs) the entity has.

## When you CREATE or EDIT a repository (`MainHub.Api/Repositories/**`)

1. Create or update the matching `<Name>RepositoryTests.cs`.
2. Every public method must have at least one test covering the happy path.
3. Cover the meaningful edge cases: not-found/null results, empty collections,
   pagination boundaries, FK/constraint violations, uniqueness, revoked/expired
   states, and enum/nullable round-tripping.
4. Follow the existing pattern: `[Collection("Postgres")]`, implement
   `IAsyncLifetime`, call `_fixture.ResetAsync()` in `InitializeAsync`, construct
   the repository from `fixture.DataSource`, and build entities via `Factories`.
5. Insert required parent rows (users, profiles, garages, …) before inserting
   child rows so foreign keys are satisfied — mirror the existing helper methods
   such as `CreateUserAndProfileAsync`.

## Before considering the change done

- Run the integration tests and make sure they pass:
  `dotnet test MainHub.Api.IntegrationTests`.
- If a public method or entity field is not exercised by a test, add the test —
  do not ship untested repository/entity code.
