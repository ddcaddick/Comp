#!/usr/bin/env node
// Builds Comp.Api (which writes its OpenAPI document to /openapi/Comp.Api.json as a build
// step — see OpenApiDocumentsDirectory in Comp.Api.csproj) and turns that document into
// clients/mobile/api/api-types.ts. CI runs this and then diffs the result against what's
// committed; a difference means the API changed shape without the client types being
// regenerated, and the build fails rather than shipping a silent mismatch.
import { execSync } from "node:child_process";
import { mkdirSync, readFileSync, writeFileSync } from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import openapiTS, { astToString } from "openapi-typescript";

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const repoRoot = path.resolve(__dirname, "..", "..");
const apiProjectPath = path.join(repoRoot, "src", "Comp.Api");
const openApiJsonPath = path.join(repoRoot, "openapi", "Comp.Api.json");
const outputPath = path.join(repoRoot, "clients", "mobile", "api", "api-types.ts");

console.log("Building Comp.Api to regenerate its OpenAPI document...");
execSync(`dotnet build "${apiProjectPath}" --configuration Release`, {
  stdio: "inherit",
  cwd: repoRoot,
});

console.log(`Reading ${path.relative(repoRoot, openApiJsonPath)}...`);
const schema = JSON.parse(readFileSync(openApiJsonPath, "utf-8"));

console.log("Generating TypeScript types...");
const ast = await openapiTS(schema);
const banner =
  "// AUTO-GENERATED — do not edit by hand.\n" +
  "// Run `npm run generate` in tools/generate-api-types to refresh this file.\n\n";
const output = banner + astToString(ast);

mkdirSync(path.dirname(outputPath), { recursive: true });
writeFileSync(outputPath, output);

console.log(`Wrote ${path.relative(repoRoot, outputPath)}`);
