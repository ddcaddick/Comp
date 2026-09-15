import { useState, type FormEvent } from "react";
import { useNavigate } from "react-router";
import { useAuth } from "../lib/auth";
import { Button } from "../components/ui/button";
import { Input } from "../components/ui/input";

export function LoginPage() {
  const { login } = useAuth();
  const navigate = useNavigate();
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);

  async function handleSubmit(event: FormEvent) {
    event.preventDefault();
    setSubmitting(true);
    setError(null);

    const result = await login(email, password);
    setSubmitting(false);

    if (result.success) {
      navigate("/home", { replace: true });
    } else {
      setError(result.error);
    }
  }

  return (
    <div className="flex min-h-screen items-center justify-center bg-muted">
      <form
        onSubmit={handleSubmit}
        className="w-full max-w-sm rounded-lg border border-border bg-background p-8 shadow-sm"
      >
        <div className="mb-3 flex items-center gap-3">
          <div className="flex h-11 w-11 items-center justify-center rounded-md bg-primary text-base font-extrabold text-primary-foreground">
            SR
          </div>
          <div className="flex flex-col leading-none">
            <span className="text-base font-extrabold tracking-widest">
              SHOOTER<span className="text-primary">RSG</span>
            </span>
            <span className="mt-1 font-mono text-[10px] uppercase tracking-wide text-muted-foreground">
              Ready. Standby. Go
            </span>
          </div>
        </div>
        <span className="mb-6 block font-mono text-[10px] uppercase tracking-widest text-muted-foreground">
          Admin console
        </span>

        <h1 className="mb-6 text-xl font-semibold">Sign in</h1>

        <label className="mb-1 block text-sm font-medium" htmlFor="email">
          Email
        </label>
        <Input
          id="email"
          type="email"
          required
          autoFocus
          value={email}
          onChange={(event) => setEmail(event.target.value)}
          className="mb-4"
        />

        <label className="mb-1 block text-sm font-medium" htmlFor="password">
          Password
        </label>
        <Input
          id="password"
          type="password"
          required
          value={password}
          onChange={(event) => setPassword(event.target.value)}
          className="mb-4"
        />

        {error && <p className="mb-4 text-sm text-destructive">{error}</p>}

        <Button type="submit" disabled={submitting} className="w-full font-bold tracking-wide uppercase">
          {submitting ? "Signing in..." : "Sign in"}
        </Button>
      </form>
    </div>
  );
}
