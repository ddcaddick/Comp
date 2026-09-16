import { useState, type FormEvent } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api } from "../lib/api";
import { useAuth } from "../lib/auth";
import { Button } from "../components/ui/button";
import { Input } from "../components/ui/input";

// The four fixed roles from the security model (Comp.Infrastructure.Identity.Roles) --
// there's no GET /roles endpoint for four values that never change.
const ROLES = ["SUPER_ADMIN", "ADMIN", "OFFICIAL", "READ_ONLY"];

function errorDetail(error: unknown, fallback: string): string {
  const detail = (error as { detail?: string | null } | undefined)?.detail;
  return detail ?? fallback;
}

export function UsersPage() {
  const { user: me } = useAuth();
  const queryClient = useQueryClient();

  const [email, setEmail] = useState("");
  const [displayName, setDisplayName] = useState("");
  const [role, setRole] = useState(ROLES[2]);
  const [password, setPassword] = useState("");
  const [error, setError] = useState<string | null>(null);

  const [editingId, setEditingId] = useState<string | null>(null);
  const [editDisplayName, setEditDisplayName] = useState("");
  const [editRole, setEditRole] = useState("");

  const [resettingId, setResettingId] = useState<string | null>(null);
  const [newPassword, setNewPassword] = useState("");

  const usersQuery = useQuery({
    queryKey: ["users"],
    queryFn: async () => {
      const { data, error } = await api.GET("/users");
      if (error) throw new Error("Failed to load users");
      return data;
    },
  });

  const createUser = useMutation({
    mutationFn: async () => {
      const { data, error, response } = await api.POST("/users", {
        body: { email, displayName, role, password },
      });
      if (error || !data) {
        throw new Error(errorDetail(error, `Could not create user (${response.status}).`));
      }
      return data;
    },
    onSuccess: () => {
      setEmail("");
      setDisplayName("");
      setRole(ROLES[2]);
      setPassword("");
      setError(null);
      queryClient.invalidateQueries({ queryKey: ["users"] });
    },
    onError: (err: Error) => setError(err.message),
  });

  const updateUser = useMutation({
    mutationFn: async (id: string) => {
      const { error, response } = await api.PATCH("/users/{id}", {
        params: { path: { id } },
        body: { displayName: editDisplayName, role: editRole },
      });
      if (error) {
        throw new Error(errorDetail(error, `Could not update user (${response.status}).`));
      }
    },
    onSuccess: () => {
      setEditingId(null);
      setError(null);
      queryClient.invalidateQueries({ queryKey: ["users"] });
    },
    onError: (err: Error) => setError(err.message),
  });

  const setActive = useMutation({
    mutationFn: async ({ id, active }: { id: string; active: boolean }) => {
      const { error, response } = await api.POST(active ? "/users/{id}/reactivate" : "/users/{id}/deactivate", {
        params: { path: { id } },
      });
      if (error) {
        throw new Error(errorDetail(error, `Could not update user (${response.status}).`));
      }
    },
    onSuccess: () => {
      setError(null);
      queryClient.invalidateQueries({ queryKey: ["users"] });
    },
    onError: (err: Error) => setError(err.message),
  });

  const resetPassword = useMutation({
    mutationFn: async (id: string) => {
      const { error, response } = await api.POST("/users/{id}/reset-password", {
        params: { path: { id } },
        body: { password: newPassword },
      });
      if (error) {
        throw new Error(errorDetail(error, `Could not reset password (${response.status}).`));
      }
    },
    onSuccess: () => {
      setResettingId(null);
      setNewPassword("");
      setError(null);
      queryClient.invalidateQueries({ queryKey: ["users"] });
    },
    onError: (err: Error) => setError(err.message),
  });

  const unlock = useMutation({
    mutationFn: async (id: string) => {
      const { error, response } = await api.POST("/users/{id}/unlock", { params: { path: { id } } });
      if (error) {
        throw new Error(errorDetail(error, `Could not unlock user (${response.status}).`));
      }
    },
    onSuccess: () => {
      setError(null);
      queryClient.invalidateQueries({ queryKey: ["users"] });
    },
    onError: (err: Error) => setError(err.message),
  });

  const setAmendPublished = useMutation({
    mutationFn: async ({ id, grant, reason }: { id: string; grant: boolean; reason: string }) => {
      const { error, response } = await api.POST("/users/{id}/amend-published", {
        params: { path: { id } },
        body: { grant, reason },
      });
      if (error) {
        throw new Error(errorDetail(error, `Could not update user (${response.status}).`));
      }
    },
    onSuccess: () => {
      setError(null);
      queryClient.invalidateQueries({ queryKey: ["users"] });
    },
    onError: (err: Error) => setError(err.message),
  });

  function handleSubmit(event: FormEvent) {
    event.preventDefault();
    createUser.mutate();
  }

  function startEdit(user: { id: string; displayName: string; role: string }) {
    setEditingId(user.id);
    setEditDisplayName(user.displayName);
    setEditRole(user.role);
    setError(null);
  }

  function cancelEdit() {
    setEditingId(null);
    setError(null);
  }

  function startReset(id: string) {
    setResettingId(id);
    setNewPassword("");
    setError(null);
  }

  function cancelReset() {
    setResettingId(null);
    setNewPassword("");
  }

  function handleToggleAmend(id: string, currentlyGranted: boolean) {
    const reason = window.prompt(
      currentlyGranted ? "Reason for revoking amend-published:" : "Reason for granting amend-published:",
    );
    if (!reason) return;
    setAmendPublished.mutate({ id, grant: !currentlyGranted, reason });
  }

  return (
    <div>
      <h1 className="mb-1 text-lg font-semibold">Users</h1>
      <p className="mb-6 text-sm text-muted-foreground">
        Create login accounts for colleagues and manage their access. There's no email sending in
        this app — after creating an account, tell the person their password directly.
      </p>

      <form
        onSubmit={handleSubmit}
        className="mb-6 flex flex-wrap items-end gap-3 rounded-lg border border-border bg-background p-4"
      >
        <div className="flex flex-col gap-1">
          <label className="text-xs font-medium text-muted-foreground" htmlFor="user-email">
            Email
          </label>
          <Input
            id="user-email"
            type="email"
            value={email}
            onChange={(e) => setEmail(e.target.value)}
            required
            className="w-56"
          />
        </div>
        <div className="flex flex-col gap-1">
          <label className="text-xs font-medium text-muted-foreground" htmlFor="user-display-name">
            Display name
          </label>
          <Input
            id="user-display-name"
            value={displayName}
            onChange={(e) => setDisplayName(e.target.value)}
            required
            className="w-48"
          />
        </div>
        <div className="flex flex-col gap-1">
          <label className="text-xs font-medium text-muted-foreground" htmlFor="user-role">
            Role
          </label>
          <select
            id="user-role"
            value={role}
            onChange={(e) => setRole(e.target.value)}
            className="h-9 w-40 rounded-md border border-border bg-input px-3 text-sm"
          >
            {ROLES.map((r) => (
              <option key={r} value={r}>
                {r}
              </option>
            ))}
          </select>
        </div>
        <div className="flex flex-col gap-1">
          <label className="text-xs font-medium text-muted-foreground" htmlFor="user-password">
            Initial password
          </label>
          <Input
            id="user-password"
            type="password"
            value={password}
            onChange={(e) => setPassword(e.target.value)}
            required
            className="w-48"
          />
        </div>
        <Button type="submit" disabled={createUser.isPending}>
          {createUser.isPending ? "Creating..." : "New user"}
        </Button>
      </form>

      {error && <p className="mb-4 text-sm text-destructive">{error}</p>}
      {usersQuery.isLoading && <p className="text-sm text-muted-foreground">Loading...</p>}
      {usersQuery.isError && <p className="text-sm text-destructive">Could not load users.</p>}

      {!usersQuery.isLoading && !usersQuery.isError && (
        <div className="overflow-x-auto">
          <table className="w-full border-collapse text-sm">
            <thead>
              <tr className="border-b border-border text-left">
                <th className="py-2 pr-4 font-medium text-muted-foreground">Email</th>
                <th className="py-2 pr-4 font-medium text-muted-foreground">Display name</th>
                <th className="py-2 pr-4 font-medium text-muted-foreground">Role</th>
                <th className="py-2 pr-4 font-medium text-muted-foreground">Status</th>
                <th className="py-2 pr-4 font-medium text-muted-foreground">Amend</th>
                <th className="py-2 pr-4 font-medium text-muted-foreground" />
              </tr>
            </thead>
            <tbody>
              {(usersQuery.data ?? []).map((user) => {
                const isSelf = user.email === me?.email;

                if (resettingId === user.id) {
                  return (
                    <tr key={user.id} className="border-b border-border bg-background">
                      <td colSpan={6} className="py-4">
                        <div className="flex flex-col gap-3">
                          <p className="text-sm font-semibold">Set a new password for {user.email}</p>
                          <div className="flex items-center gap-3">
                            <Input
                              type="password"
                              value={newPassword}
                              onChange={(e) => setNewPassword(e.target.value)}
                              placeholder="New password"
                              className="max-w-xs"
                            />
                            <Button
                              size="sm"
                              disabled={!newPassword.trim() || resetPassword.isPending}
                              onClick={() => resetPassword.mutate(user.id)}
                            >
                              {resetPassword.isPending ? "Saving..." : "Set password"}
                            </Button>
                            <Button variant="outline" size="sm" onClick={cancelReset}>
                              Cancel
                            </Button>
                          </div>
                        </div>
                      </td>
                    </tr>
                  );
                }

                if (editingId === user.id) {
                  return (
                    <tr key={user.id} className="border-b border-border bg-background">
                      <td className="py-2 pr-4">{user.email}</td>
                      <td className="py-2 pr-4">
                        <Input
                          value={editDisplayName}
                          onChange={(e) => setEditDisplayName(e.target.value)}
                          className="h-8 w-40"
                        />
                      </td>
                      <td className="py-2 pr-4">
                        <select
                          value={editRole}
                          onChange={(e) => setEditRole(e.target.value)}
                          disabled={isSelf}
                          className="h-8 w-36 rounded-md border border-border bg-input px-2 text-sm disabled:opacity-50"
                        >
                          {ROLES.map((r) => (
                            <option key={r} value={r}>
                              {r}
                            </option>
                          ))}
                        </select>
                      </td>
                      <td className="py-2 pr-4">{user.isActive ? "Active" : "Deactivated"}</td>
                      <td className="py-2 pr-4">{user.canAmendPublished ? "Yes" : "No"}</td>
                      <td className="py-2 pr-4">
                        <div className="flex gap-2">
                          <Button
                            size="sm"
                            disabled={!editDisplayName.trim() || updateUser.isPending}
                            onClick={() => updateUser.mutate(user.id)}
                          >
                            {updateUser.isPending ? "Saving..." : "Save"}
                          </Button>
                          <Button variant="outline" size="sm" onClick={cancelEdit}>
                            Cancel
                          </Button>
                        </div>
                      </td>
                    </tr>
                  );
                }

                return (
                  <tr key={user.id} className="border-b border-border">
                    <td className="py-2 pr-4">{user.email}</td>
                    <td className="py-2 pr-4">
                      {user.displayName}
                      {isSelf && <span className="ml-2 text-xs text-muted-foreground">(you)</span>}
                    </td>
                    <td className="py-2 pr-4">{user.role}</td>
                    <td className="py-2 pr-4">
                      {user.isActive ? "Active" : "Deactivated"}
                      {user.isLockedOut && <span className="ml-2 text-xs text-destructive">Locked out</span>}
                    </td>
                    <td className="py-2 pr-4">{user.canAmendPublished ? "Yes" : "No"}</td>
                    <td className="py-2 pr-4">
                      <div className="flex flex-wrap items-center gap-3">
                        <Button variant="outline" size="sm" className="w-16" onClick={() => startEdit(user)}>
                          Edit
                        </Button>
                        <Button
                          variant="outline"
                          size="sm"
                          className="w-32 whitespace-nowrap"
                          onClick={() => startReset(user.id)}
                        >
                          Reset password
                        </Button>
                        {user.isLockedOut && (
                          <Button
                            variant="outline"
                            size="sm"
                            className="w-20"
                            disabled={unlock.isPending}
                            onClick={() => unlock.mutate(user.id)}
                          >
                            Unlock
                          </Button>
                        )}
                        <Button
                          variant="outline"
                          size="sm"
                          className="w-36 whitespace-nowrap"
                          disabled={setAmendPublished.isPending}
                          onClick={() => handleToggleAmend(user.id, user.canAmendPublished)}
                        >
                          {user.canAmendPublished ? "Revoke amend" : "Grant amend"}
                        </Button>
                        {user.isActive ? (
                          <Button
                            variant="destructive"
                            size="sm"
                            className="w-24"
                            disabled={isSelf || setActive.isPending}
                            onClick={() => setActive.mutate({ id: user.id, active: false })}
                          >
                            Deactivate
                          </Button>
                        ) : (
                          <Button
                            variant="outline"
                            size="sm"
                            className="w-24"
                            disabled={setActive.isPending}
                            onClick={() => setActive.mutate({ id: user.id, active: true })}
                          >
                            Reactivate
                          </Button>
                        )}
                      </div>
                    </td>
                  </tr>
                );
              })}
              {(usersQuery.data ?? []).length === 0 && (
                <tr>
                  <td colSpan={6} className="py-4 text-center text-muted-foreground">
                    No users yet.
                  </td>
                </tr>
              )}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}
