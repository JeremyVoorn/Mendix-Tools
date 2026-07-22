import React from "react";

/**
 * Compact data table for environments, backups, packages, DB list.
 * Columns: [{key,header,width,align,mono,render}]. Rows are plain objects.
 * Supports row selection, hover, and a right-aligned actions column via render.
 */
export function DataTable({ columns = [], rows = [], getRowId, selectable = false, selected = [], onSelectChange, onRowClick, dense = false, emptyText = "No rows", style, ...rest }) {
  const rowId = getRowId || ((r, i) => r.id ?? i);
  const allChecked = rows.length > 0 && selected.length === rows.length;
  const someChecked = selected.length > 0 && !allChecked;

  const toggleAll = () => onSelectChange?.(allChecked ? [] : rows.map(rowId));
  const toggleOne = (id) => onSelectChange?.(selected.includes(id) ? selected.filter((s) => s !== id) : [...selected, id]);

  const cellPad = dense ? "7px 12px" : "11px 14px";

  return (
    <div style={{ border: "var(--border-w) solid var(--border)", borderRadius: "var(--radius-lg)", overflow: "hidden", background: "var(--bg-surface)", ...style }} {...rest}>
      <table style={{ width: "100%", borderCollapse: "collapse", fontSize: "var(--text-sm)" }}>
        <thead>
          <tr style={{ background: "var(--bg-subtle)" }}>
            {selectable && (
              <th style={{ width: 40, padding: cellPad, textAlign: "left" }}>
                <input type="checkbox" checked={allChecked} ref={(el) => el && (el.indeterminate = someChecked)} onChange={toggleAll} style={{ cursor: "pointer" }} />
              </th>
            )}
            {columns.map((c) => (
              <th key={c.key} style={{
                padding: cellPad, textAlign: c.align || "left", width: c.width,
                fontSize: "var(--text-2xs)", fontWeight: "var(--fw-semibold)", letterSpacing: "0.06em",
                textTransform: "uppercase", color: "var(--text-tertiary)",
                borderBottom: "var(--border-w) solid var(--border)", whiteSpace: "nowrap",
              }}>{c.header}</th>
            ))}
          </tr>
        </thead>
        <tbody>
          {rows.length === 0 ? (
            <tr><td colSpan={columns.length + (selectable ? 1 : 0)} style={{ padding: "32px", textAlign: "center", color: "var(--text-tertiary)" }}>{emptyText}</td></tr>
          ) : rows.map((r, i) => {
            const id = rowId(r, i);
            const isSel = selected.includes(id);
            return (
              <Row key={id} onClick={onRowClick ? () => onRowClick(r) : undefined} clickable={!!onRowClick} selected={isSel} last={i === rows.length - 1}>
                {selectable && (
                  <td style={{ padding: cellPad }} onClick={(e) => e.stopPropagation()}>
                    <input type="checkbox" checked={isSel} onChange={() => toggleOne(id)} style={{ cursor: "pointer" }} />
                  </td>
                )}
                {columns.map((c) => (
                  <td key={c.key} style={{
                    padding: cellPad, textAlign: c.align || "left", color: "var(--text-primary)",
                    fontFamily: c.mono ? "var(--font-mono)" : "var(--font-sans)",
                    whiteSpace: c.wrap ? "normal" : "nowrap",
                  }}>{c.render ? c.render(r[c.key], r) : r[c.key]}</td>
                ))}
              </Row>
            );
          })}
        </tbody>
      </table>
    </div>
  );
}

function Row({ children, onClick, clickable, selected, last }) {
  const [hover, setHover] = React.useState(false);
  return (
    <tr
      onClick={onClick}
      onMouseEnter={() => setHover(true)}
      onMouseLeave={() => setHover(false)}
      style={{
        cursor: clickable ? "pointer" : "default",
        background: selected ? "var(--accent-subtle)" : hover && clickable ? "var(--bg-hover)" : "transparent",
        borderBottom: last ? "none" : "var(--border-w) solid var(--border-subtle)",
        transition: "background var(--duration-fast) var(--ease-standard)",
      }}
    >{children}</tr>
  );
}
