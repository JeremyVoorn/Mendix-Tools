import React from "react";

/**
 * Surface container for grouping content — the primary building block of every
 * Mendix Tools screen. Optional header (title + actions) and padded body.
 */
export function Card({ title, subtitle, actions, children, padding = "16px", interactive = false, style, bodyStyle, ...rest }) {
  const [hover, setHover] = React.useState(false);
  return (
    <div
      onMouseEnter={() => interactive && setHover(true)}
      onMouseLeave={() => interactive && setHover(false)}
      style={{
        background: "var(--bg-surface)",
        border: "var(--border-w) solid var(--border)",
        borderRadius: "var(--radius-xl)",
        boxShadow: hover ? "var(--shadow-md)" : "var(--shadow-sm)",
        borderColor: hover ? "var(--border-strong)" : "var(--border)",
        transition: "box-shadow var(--duration-normal) var(--ease-standard), border-color var(--duration-normal) var(--ease-standard)",
        cursor: interactive ? "pointer" : "default",
        overflow: "hidden",
        ...style,
      }}
      {...rest}
    >
      {(title || actions) && (
        <div style={{
          display: "flex", alignItems: "center", justifyContent: "space-between", gap: "12px",
          padding: "14px 16px", borderBottom: "var(--border-w) solid var(--border-subtle)",
        }}>
          <div style={{ display: "flex", flexDirection: "column", gap: "2px", minWidth: 0 }}>
            {title && <span style={{ fontSize: "var(--text-md)", fontWeight: "var(--fw-semibold)", color: "var(--text-primary)" }}>{title}</span>}
            {subtitle && <span style={{ fontSize: "var(--text-xs)", color: "var(--text-tertiary)" }}>{subtitle}</span>}
          </div>
          {actions && <div style={{ display: "flex", alignItems: "center", gap: "6px", flexShrink: 0 }}>{actions}</div>}
        </div>
      )}
      <div style={{ padding, ...bodyStyle }}>{children}</div>
    </div>
  );
}
