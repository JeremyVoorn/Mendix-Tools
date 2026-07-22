import React from "react";

/**
 * Lightweight hover tooltip. Wraps a single child; shows `label` on hover/focus.
 * CSS-positioned, no portal — fine for toolbars and truncated cells.
 */
export function Tooltip({ label, placement = "top", children, style }) {
  const [show, setShow] = React.useState(false);
  const pos = {
    top: { bottom: "calc(100% + 6px)", left: "50%", transform: "translateX(-50%)" },
    bottom: { top: "calc(100% + 6px)", left: "50%", transform: "translateX(-50%)" },
    left: { right: "calc(100% + 6px)", top: "50%", transform: "translateY(-50%)" },
    right: { left: "calc(100% + 6px)", top: "50%", transform: "translateY(-50%)" },
  }[placement];

  return (
    <span
      style={{ position: "relative", display: "inline-flex", ...style }}
      onMouseEnter={() => setShow(true)}
      onMouseLeave={() => setShow(false)}
      onFocus={() => setShow(true)}
      onBlur={() => setShow(false)}
    >
      {children}
      {show && (
        <span role="tooltip" style={{
          position: "absolute", zIndex: "var(--z-tooltip)", ...pos,
          padding: "5px 9px", background: "var(--slate-900)", color: "#fff",
          fontSize: "var(--text-xs)", fontFamily: "var(--font-sans)", lineHeight: 1.35,
          borderRadius: "var(--radius-md)", boxShadow: "var(--shadow-md)",
          whiteSpace: "nowrap", pointerEvents: "none",
          animation: "mxt-tip var(--duration-fast) var(--ease-out)",
        }}>
          {label}
          <style>{"@keyframes mxt-tip{from{opacity:0}}"}</style>
        </span>
      )}
    </span>
  );
}
