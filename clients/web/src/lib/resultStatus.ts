// Shared between the on-screen results table and the results PDF so "NotRun" (the raw
// enum member name, as returned by the API) never leaks into either display verbatim.
export function formatResultStatus(status: string): string {
  return status === "NotRun" ? "Not Run" : status;
}
