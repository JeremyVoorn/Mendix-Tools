import React from "react";

const colors = {
  running: "var(--success)", success: "var(--success)", online: "var(--success)",
  stopped: "var(--text-tertiary)", offline: "var(--text-tertiary)", idle: "var(--text-tertiary)",
  warning: "var(--warning)", degraded: "var(--warning)", pending: "var(--warning)",
  error: "var(--danger)", failed: "var(--danger)",
  busy: "var(--accent)", deploying: "var(--accent)", running_task: "var(--accent)",
};

/**
 * Small colored status dot, optionally pulsing for in-progress states.
 * Pair with a text label for env/task status in tables and headers.
 */
export function StatusDot({ status = "idle", pulse = false, size = 8, label, style, ...rest }) {
  const color = colors[status] || "var(--text-tertiary)";
  const dot = (
    <span style={{ position: "relative", display: "inline-flex", width: size, height: size, flexShrink: 0 }}>
      {pulse && <span style={{
        position: "absolute", inset: 0, borderRadius: "50%", background: color, opacity: 0.5,
        animation: "mxt-ping 1.4s cubic-bezier(0,0,0.2,1) infinite",
      }} />}
      <span style={{ width: size, height: size, borderRadius: "50%", background: color }} />
      <style>{"@keyframes mxt-ping{75%,100%{transform:scale(2.2);opacity:0}}"}</style>
    </span>
  );
  if (!label) return React.cloneElement(dot, { style: { ...dot.props.style, ...style }, ...rest });
  return (
    <span style={{ display: "inline-flex", alignItems: "center", gap: "7px", ...style }} {...rest}>
      {dot}
      <span style={{ fontSize: "var(--text-sm)", color: "var(--text-secondary)" }}>{label}</span>
    </span>
  );
}
