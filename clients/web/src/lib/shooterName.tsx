interface NamedShooter {
  firstName: string;
  lastName: string;
  nickname?: string | null;
}

export function fullName(shooter: NamedShooter): string {
  return `${shooter.firstName} ${shooter.lastName}`;
}

/** Plain-text version for contexts with no hover affordance, e.g. the generated PDF. */
export function preferredName(shooter: NamedShooter): string {
  return shooter.nickname?.trim() ? shooter.nickname : fullName(shooter);
}

/** On-screen version: shows the nickname when set, with the full name as a hover title —
 * falls back to just the full name (no tooltip) when there's no nickname to shorten it to. */
export function ShooterName({ shooter }: { shooter: NamedShooter }) {
  if (shooter.nickname?.trim()) {
    return <span title={fullName(shooter)}>{shooter.nickname}</span>;
  }
  return <>{fullName(shooter)}</>;
}
