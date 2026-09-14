import { useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { createColumnHelper, flexRender, getCoreRowModel, useReactTable } from "@tanstack/react-table";
import type { components } from "@comp/api-types";
import { api } from "../lib/api";
import { Input } from "../components/ui/input";

type Shooter = components["schemas"]["ShooterResponse"];

const columnHelper = createColumnHelper<Shooter>();

const columns = [
  columnHelper.accessor("firstName", { header: "First name" }),
  columnHelper.accessor("lastName", { header: "Last name" }),
  columnHelper.accessor("nickname", { header: "Nickname", cell: (info) => info.getValue() ?? "—" }),
  columnHelper.accessor("membershipNo", { header: "Membership no.", cell: (info) => info.getValue() ?? "—" }),
  columnHelper.accessor("isActive", {
    header: "Status",
    cell: (info) => (info.getValue() ? "Active" : "Deactivated"),
  }),
];

export function ShootersPage() {
  const [search, setSearch] = useState("");

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
            {table.getRowModel().rows.map((row) => (
              <tr key={row.id} className="border-b border-border">
                {row.getVisibleCells().map((cell) => (
                  <td key={cell.id} className="py-2 pr-4">
                    {flexRender(cell.column.columnDef.cell, cell.getContext())}
                  </td>
                ))}
              </tr>
            ))}
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
