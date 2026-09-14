import { clsx, type ClassValue } from "clsx";
import { twMerge } from "tailwind-merge";

/** The standard shadcn/ui helper: merges conditional class names without Tailwind conflicts. */
export function cn(...inputs: ClassValue[]) {
  return twMerge(clsx(inputs));
}
