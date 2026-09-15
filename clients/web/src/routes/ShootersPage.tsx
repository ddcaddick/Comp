import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { createColumnHelper, flexRender, getCoreRowModel, useReactTable } from "@tanstack/react-table";
import type { components } from "@comp/api-types";
import { api } from "../lib/api";
import { Button } from "../components/ui/button";
import { Input } from "../components/ui/input";

type Shooter = components["schemas"]["ShooterResponse"];

const columnHelper = createColumnHelper<Shooter>();

function errorDetail(error: unknown, fallback: string): string {
  const detail = (error as { detail?: string | null } | undefined)?.detail;
  return detail ?? fallback;
}

export function ShootersPage() {
  const [search, setSearch] = useState("");
  const [editingId, setEditingId] = useState<string | null>(null);
  const [firstName, setFirstName] = useState("");
  const [lastName, setLastName] = useState("");
  const [nickname, setNickname] = useState("");
  const [membershipNo, setMembershipNo] = useState("");
  const [error, setError] = useState<string | null>(null);
  const queryClient = useQueryClient();

  const { data, isLoading, isError } = useQuery({
    queryKey: ["shooters", search],
    queryFn: async () => {
      const { data, error } = await api.GET("/shooters", {
        params: { query: { q: search || undefined } },
      });
      if (error) {
        throw new Error("Failed to load shooters");
      }
      return data;
    },
  });

  function startEdit(shooter: Shooter) {
    setEditingId(shooter.id);
    setFirstName(shooter.firstName);
    setLastName(shooter.lastName);
    setNickname(shooter.nickname ?? "");
    setMembershipNo(shooter.membershipNo ?? "");
    setError(null);
  }

  function cancelEdit() {
    setEditingId(null);
    setError(null);
  }

  const updateShooter = useMutation({
    mutationFn: async (id: string) => {
      const { data, error, response } = await api.PATCH("/shooters/{id}", {
        params: { path: { id } },
        body: {
          firstName: firstName.trim(),
          lastName: lastName.trim(),
          nickname: nickname.trim() || null,
          membershipNo: membershipNo.trim() || null,
        },
      });
      if (error || !data) {
        throw new Error(errorDetail(error, `Could not update this shooter (${response.status}).`));
      }
      return data;
    },
    onSuccess: () => {
      setEditingId(null);
      setError(null);
      queryClient.invalidateQueries({ queryKey: ["shooters"] });
    },
    onError: (err: Error) => setError(err.message),
  });

  const columns = [
    columnHelper.accessor("firstName", { header: "First name" }),
    columnHelper.accessor("lastName", { header: "Last name" }),
    columnHelper.accessor("nickname", { header: "Nickname", cell: (info) => info.getValue() ?? "—" }),
    columnHelper.accessor("membershipNo", { header: "Membership no.", cell: (info) => info.getValue() ?? "—" }),
    columnHelper.accessor("isActive", {
      header: "Status",
      cell: (info) => (info.getValue() ? "Active" : "Deactivated"),
    }),
    columnHelper.display({
      id: "actions",
      header: "",
      cell: (info) => (
        <Button variant="outline" size="sm" onClick={() => startEdit(info.row.original)}>
          Edit
        </Button>
      ),
    }),
  ];

  const table = useReactTable({
    data: data ?? [],
    columns,
    getCoreRowModel: getCoreRowModel(),
  });

  return (
    <div>
      <h1 className="mb-4 text-lg font-semibold">Shooters</h1>
      <Input
        placeholder="Search by name or nickname..."
        value={search}
        onChange={(event) => setSearch(event.target.value)}
        className="mb-4 max-w-sm"
      />

      {error && <p className="mb-4 text-sm text-destructive">{error}</p>}
      {isLoading && <p className="text-sm text-muted-foreground">Loading...</p>}
      {isError && <p className="text-sm text-destructive">Could not load shooters.</p>}

      {!isLoading && !isError && (
        <table className="w-full border-collapse text-sm">
          <thead>
            {table.getHeaderGroups().map((headerGroup) => (
              <tr key={headerGroup.id} className="border-b border-border text-left">
                {headerGroup.headers.map((header) => (
                  <th key={header.id} className="py-2 pr-4 font-medium text-muted-foreground">
                    {flexRender(header.column.columnDef.header, header.getContext())}
                  </th>
                ))}
              </tr>
            ))}
          </thead>
          <tbody>
            {table.getRowModel().rows.map((row) => {
              if (row.original.id !== editingId) {
                return (
                  <tr key={row.id} className="border-b border-border">
                    {row.getVisibleCells().map((cell) => (
                      <td key={cell.id} className="py-2 pr-4">
                        {flexRender(cell.column.columnDef.cell, cell.getContext())}
                      </td>
                    ))}
                  </tr>
                );
              }

              return (
                <tr key={row.id} className="border-b border-border bg-background">
                  <td className="py-2 pr-4">
                    <Input value={firstName} onChange={(e) => setFirstName(e.target.value)} className="h-8" />
                  </td>
                  <td className="py-2 pr-4">
                    <Input value={lastName} onChange={(e) => setLastName(e.target.value)} className="h-8" />
                  </td>
                  <td className="py-2 pr-4">
                    <Input
                      value={nickname}
                      onChange={(e) => setNickname(e.target.value)}
                      placeholder="—"
                      className="h-8"
                    />
                  </td>
                  <td className="py-2 pr-4">
                    <Input
                      value={membershipNo}
                      onChange={(e) => setMembershipNo(e.target.value)}
                      placeholder="—"
                      className="h-8"
                    />
                  </td>
                  <td className="py-2 pr-4">{row.original.isActive ? "Active" : "Deactivated"}</td>
                  <td className="py-2 pr-4">
                    <div className="flex gap-2">
                      <Button
                        size="sm"
                        disabled={!firstName.trim() || !lastName.trim() || updateShooter.isPending}
                        onClick={() => updateShooter.mutate(row.original.id)}
                      >
                        {updateShooter.isPending ? "Saving..." : "Save"}
                      </Button>
                      <Button variant="outline" size="sm" onClick={cancelEdit}>
                        Cancel
                      </Button>
                    </div>
                  </td>
                </tr>
              );
            })}
            {table.getRowModel().rows.length === 0 && (
              <tr>
                <td colSpan={columns.length} className="py-4 text-center text-muted-foreground">
                  No shooters found.
                </td>
              </tr>
            )}
          </tbody>
        </table>
      )}
    </div>
  );
}
