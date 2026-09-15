// Local calendar dates, not UTC -- a club's "today" is whatever day it is where the club
// actually is, and toISOString()/Date math done in UTC drifts a day around midnight in most
// timezones. Event dates are already plain YYYY-MM-DD strings (no time component), so all
// comparisons here stay in that same string space rather than round-tripping through Date.
export function todayIso(): string {
  return dateToIso(new Date());
}

export function addDaysIso(iso: string, days: number): string {
  const [year, month, day] = iso.split("-").map(Number);
  const date = new Date(year, month - 1, day);
  date.setDate(date.getDate() + days);
  return dateToIso(date);
}

function dateToIso(date: Date): string {
  const year = date.getFullYear();
  const month = String(date.getMonth() + 1).padStart(2, "0");
  const day = String(date.getDate()).padStart(2, "0");
  return `${year}-${month}-${day}`;
}
