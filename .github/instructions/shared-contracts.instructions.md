---
applyTo: "Shared.Contracts/**,MainHub.Api/**"
---

# Shared Contracts (DTOs) approach

`Shared.Contracts` is a standalone .NET project that holds the API's public
data contracts (DTOs + enums). It is the **single source of truth** for shapes
exchanged over the wire: the backend references it directly, and a TypeScript
npm package is generated from it in CI and published for the UI to consume.

Data flows in one direction only:

```
C# DTOs/enums  ──►  MainHub.Api (ProjectReference)
      │
      └──►  TypeGen  ──►  generated/*.ts  ──►  npm package  ──►  UI
```

## Project layout

- `DTOs/**` — one C# class per DTO. Related DTOs may be grouped in subfolders
  (e.g. `DTOs/ServiceHistory/`).
- `Enums.cs` — shared enums (`WheelDriveType`, `FuelType`, `TransmissionType`, …).
- `Generator.cs` — TypeGen generation spec; do not hand-edit unless changing the
  export strategy.
- `tgconfig.json` — TypeGen config (`generationSpecs`, `outputPath: generated`).
- `.config/dotnet-tools.json` — pins the `dotnet-typegen` local tool.
- `package.json` — npm package manifest (`@rookit-group/shared-contracts`).
- `generated/` — TypeGen output. **Gitignored and produced in CI only** — never
  commit it and never edit generated `.ts` files by hand.

## Authoring DTOs — rules

- **Contracts live here, nowhere else.** Any type sent to or received from a
  client belongs in `Shared.Contracts`, never inline in `MainHub.Api`. The API
  references this project (`ProjectReference`), so add/change the DTO here first.
- **Every DTO must be a top-level `public` class.** `Generator.cs` exports every
  public, non-nested type in the assembly. Types that are not `public`, or are
  nested, are silently skipped from TypeScript generation.
- **Enums go in `Enums.cs`** (or the `Shared.Contracts.Enums` namespace) and are
  emitted as TypeScript enums. Reference them from DTOs rather than using magic
  strings/ints.
- **Namespaces:** DTOs use `Shared.Contracts.DTOs` (or a nested namespace such as
  `Shared.Contracts.DTOs.ServiceHistory`); enums use `Shared.Contracts.Enums`.
- **Use `required` for non-nullable members** so both C# and the generated TS
  reflect mandatory fields; use nullable types (`DateTime?`, `List<string>?`)
  for optional ones.
- **Keep them POCOs.** Plain properties only — no behavior, validation
  attributes, EF mappings, or business logic. Request validation belongs in
  `MainHub.Api/Validators`; persistence/domain types stay in the API project.
- **Document with XML doc comments** (`/// <summary>`); they clarify intent for
  API and UI developers.
- **Generics are supported** (e.g. `PagedResultDto<TItem>`) and map to generic
  TypeScript interfaces.
- **Naming:** suffix DTOs with `Dto` and match the file name to the class name
  (`VehicleDto` → `VehicleDto.cs`). TypeGen emits kebab-case files
  (`vehicle-dto.ts`) with camelCase members automatically.

## Type mapping notes (C# → TypeScript)

- `Guid`/`string` → `string`, `int`/`decimal` → `number`, `bool` → `boolean`.
- `DateTime`/`DateTime?` → `Date`.
- `List<T>` / `IReadOnlyList<T>` → `T[]`.
- Enums → TypeScript `enum` in their own file.

## Generation & publishing (CI)

Handled by `.github/workflows/publish-contracts.yml`, triggered on pushes to
`dev` that touch `Shared.Contracts/**` (or via manual `workflow_dispatch`):

1. `dotnet tool restore` then `dotnet build --configuration Release`.
2. `dotnet typegen generate` → writes `generated/*.ts`.
3. Copies `package.json` into `generated/`.
4. Builds an `index.ts` barrel that re-exports every generated file.
5. Versions the package as `0.0.<github.run_number>` (no git tag).
6. `npm publish` to the GitHub Packages registry
   (`https://npm.pkg.github.com`, scope `@rookit-group`).

Because generation and publishing are fully automated, a normal contract change
is just: **edit/add the C# DTO or enum, keep it top-level `public`, and let CI
regenerate and publish.** Do not run TypeGen to commit output into the repo.

## When adding or changing a contract — checklist

1. Add/update the C# DTO under `DTOs/**` (or the enum in `Enums.cs`).
2. Confirm the type is top-level and `public`.
3. Update the corresponding code in `MainHub.Api` (endpoints, services,
   validators) that produces/consumes it.
4. If members are mandatory, mark them `required`; if optional, make them
   nullable.
5. Do **not** touch `generated/` or the published package version — CI owns both.
