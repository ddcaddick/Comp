# generate-api-types

Generates `clients/packages/api-types/src/index.ts` from `Comp.Api`'s OpenAPI document —
a workspace package both the web and mobile clients depend on. This tool itself is not
part of the `clients/` pnpm workspace; it's a build tool, not client code.

## Usage

```powershell
cd tools/generate-api-types
npm install
npm run generate
```

This builds `Comp.Api` (which writes `openapi/Comp.Api.json` as a build step — see
`OpenApiDocumentsDirectory` in `Comp.Api.csproj`), then runs `openapi-typescript` against
that document and writes the result to `clients/packages/api-types/src/index.ts`.

## Why this exists

A renamed field or a changed nullable in the API should be a compile error in a client,
not a runtime surprise at the range. CI regenerates this file on every run and fails the
build if the result differs from what's committed — see the `openapi-types` job in
`.github/workflows/ci.yml`. If your PR changes a `Comp.Contracts` DTO or an endpoint's
shape, run this locally and commit the updated `index.ts` alongside your change.
