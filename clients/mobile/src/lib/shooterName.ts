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

/**
 * Sorting a list of shooters by surname stops making sense once nicknames are shown
 * instead -- "Aron Chatwin" filed under C reads oddly next to his own displayed name,
 * "Azza". This key sorts on whatever preferredName would actually display: the
 * nickname when there is one, and the surname (not the full "First Last" string)
 * otherwise, per explicit user direction.
 */
export function preferredNameSortKey(shooter: NamedShooter): string {
  return (shooter.nickname?.trim() ? shooter.nickname : shooter.lastName).toLowerCase();
}
