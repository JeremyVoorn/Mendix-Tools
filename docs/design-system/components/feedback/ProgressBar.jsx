import React from "react";

/**
 * Determinate/indeterminate progress bar for long-running tasks (backup
 * download, restore, build, deploy). Shows an optional label + percentage.
 */
export function ProgressBar({ value = 0, max = 100, label, tone = "accent", showValue = true, indeterminate = false, size = "md", style, ...rest }) {
  const pct = Math.min(100, Math.max(0, (value / max) * 100));
  const color = { accent: "var(--accent)", success: "var(--success)", warning: "var(--warning)", danger: "var(--danger)", db: "var(--db)", package: "var(--package)" }[tone] || "var(--accent)";
  const h = size === "sm" ? 4 : size === "lg" ? 10 : 6;

  return (
    <div style={{ display: "flex", flexDirection: "column", gap: "6px", width: "100%", ...style }} {...rest}>
      {(label || (showValue && !indeterminate)) && (
        <div style={{ display: "flex", justifyContent: "space-between", alignItems: "baseline", gap: "12px" }}>
          {label && <span style={{ fontSize: "var(--text-sm)", color: "var(--text-secondary)" }}>{label}</span>}
          {showValue && !indeterminate && (
            <span style={{ fontSize: "var(--text-xs)", fontFamily: "var(--font-mono)", color: "var(--text-tertiary)" }}>{Math.round(pct)}%</span>
          )}
        </div>
      )}
      <div style={{ height: h, borderRadius: "var(--radius-full)", background: "var(--bg-inset)", overflow: "hidden", position: "relative" }}>
        {indeterminate ? (
          <div style={{ position: "absolute", inset: 0, width: "40%", background: color, borderRadius: "var(--radius-full)", animation: "mxt-indet 1.3s var(--ease-in-out) infinite" }} />
        ) : (
          <div style={{ height: "100%", width: `${pct}%`, background: color, borderRadius: "var(--radius-full)", transition: "width var(--duration-slow) var(--ease-standard)" }} />
        )}
        <style>{"@keyframes mxt-indet{0%{left:-40%}100%{left:100%}}"}</style>
      </div>
    </div>
  );
}
