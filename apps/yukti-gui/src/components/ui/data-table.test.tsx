import { describe, it, expect } from "vitest";
import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { DataTable, type Column } from "./data-table";

interface Row {
  id: string;
  name: string;
  count: number;
}

const rows: Row[] = [
  { id: "b", name: "Bravo", count: 2 },
  { id: "a", name: "Alpha", count: 1 },
  { id: "c", name: "Charlie", count: 3 },
];

const columns: Column<Row>[] = [
  { key: "name", header: "Name", sortable: true, sortValue: (r) => r.name, render: (r) => r.name },
  { key: "count", header: "Count", render: (r) => String(r.count) },
];

describe("DataTable", () => {
  it("shows the empty state when there are no rows", () => {
    render(<DataTable columns={columns} rows={[]} rowKey={(r) => r.id} emptyTitle="Nothing here" />);
    expect(screen.getByText("Nothing here")).toBeInTheDocument();
  });

  it("renders one row per item in the given order by default", () => {
    render(<DataTable columns={columns} rows={rows} rowKey={(r) => r.id} />);
    const cells = screen.getAllByText(/Bravo|Alpha|Charlie/);
    expect(cells.map((c) => c.textContent)).toEqual(["Bravo", "Alpha", "Charlie"]);
  });

  it("sorts by a sortable column when its header button is clicked", async () => {
    const user = userEvent.setup();
    render(<DataTable columns={columns} rows={rows} rowKey={(r) => r.id} />);
    await user.click(screen.getByRole("button", { name: /Name/ }));
    const cells = screen.getAllByText(/Bravo|Alpha|Charlie/);
    expect(cells.map((c) => c.textContent)).toEqual(["Alpha", "Bravo", "Charlie"]);
  });

  it("marks the sorted column with aria-sort", async () => {
    const user = userEvent.setup();
    render(<DataTable columns={columns} rows={rows} rowKey={(r) => r.id} />);
    const header = screen.getByRole("columnheader", { name: /Name/ });
    expect(header).toHaveAttribute("aria-sort", "none");
    await user.click(screen.getByRole("button", { name: /Name/ }));
    expect(header).toHaveAttribute("aria-sort", "ascending");
  });

  it("expands a row to show expandedContent when its toggle is clicked", async () => {
    const user = userEvent.setup();
    render(
      <DataTable
        columns={columns}
        rows={rows}
        rowKey={(r) => r.id}
        expandedContent={(r) => <div>Details for {r.name}</div>}
      />,
    );
    expect(screen.queryByText("Details for Bravo")).not.toBeInTheDocument();
    const toggles = screen.getAllByRole("button", { name: "Toggle row" });
    await user.click(toggles[0]);
    expect(screen.getByText("Details for Bravo")).toBeInTheDocument();
  });

  it("paginates rows and marks the active page with aria-current", async () => {
    const user = userEvent.setup();
    const manyRows = Array.from({ length: 5 }, (_, i) => ({ id: String(i), name: `Row ${i}`, count: i }));
    render(<DataTable columns={columns} rows={manyRows} rowKey={(r) => r.id} pageSize={2} />);
    expect(screen.getByText("Row 0")).toBeInTheDocument();
    expect(screen.queryByText("Row 2")).not.toBeInTheDocument();

    const nav = screen.getByRole("navigation", { name: "Pagination" });
    const page2 = within(nav).getByRole("button", { name: "Page 2" });
    await user.click(page2);
    expect(page2).toHaveAttribute("aria-current", "page");
    expect(screen.getByText("Row 2")).toBeInTheDocument();
  });
});
