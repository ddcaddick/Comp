// Mirrors clients/web/src/lib/shooterName.tsx's preferredName -- shows a shooter's
// nickname when one is set, falling back to their full name otherwise. React Native has
// no hover-title affordance, so unlike the web version there's no separate on-screen
// variant that also surfaces the full name.
interface NamedShooter {
  firstName: string;
  lastName: string;
  nickname?: string | null;
}

export function preferredName(shooter: NamedShooter): string {
  return shooter.nickname?.trim() ? shooter.nickname : `${shooter.firstName} ${shooter.lastName}`;
}
