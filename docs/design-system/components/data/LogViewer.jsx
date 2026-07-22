import React from "react";

const levelColor = {
  info: "var(--text-secondary)",
  success: "var(--success)",
  warn: "var(--warning)",
  warning: "var(--warning)",
  error: "var(--danger)",
  debug: "var(--text-tertiary)",
  cmd: "var(--accent-text)",
};

/**
 * Terminal-style log/console output for builds, deploys, restore jobs.
 * `lines` are [{time,level,text}] or plain strings. Dark surface in both themes,
 * monospace, auto-scrolls to bottom when `follow` is set.
 */
export function LogViewer({ lines = [], follow = true, height = 260, title, showTimestamps = true, style, ...rest }) {
  const ref = React.useRef(null);
  React.useEffect(() => {
    if (follow && ref.current) ref.current.scrollTop = ref.current.scrollHeight;
  }, [lines, follow]);

  return (
    <div style={{
      border: "var(--border-w) solid var(--border)", borderRadius: "var(--radius-lg)",
      overflow: "hidden", background: "var(--code-bg)", ...style,
    }} {...rest}>
      {title && (
        <div style={{
          display: "flex", alignItems: "center", gap: "8px", padding: "8px 12px",
          borderBottom: "1px solid rgba(255,255,255,0.08)", background: "rgba(255,255,255,0.03)",
        }}>
          <i data-lucide="terminal" style={{ width: 14, height: 14, color: "var(--code-text)" }} />
          <span style={{ fontSize: "var(--text-xs)", fontFamily: "var(--font-mono)", color: "var(--code-text)" }}>{title}</span>
        </div>
      )}
      <div ref={ref} style={{
        height, overflow: "auto", padding: "10px 12px",
        fontFamily: "var(--font-mono)", fontSize: "var(--text-xs)", lineHeight: 1.7, color: "var(--code-text)",
      }}>
        {lines.map((l, i) => {
          const line = typeof l === "string" ? { text: l, level: "info" } : l;
          return (
            <div key={i} style={{ display: "flex", gap: "10px", whiteSpace: "pre-wrap", wordBreak: "break-word" }}>
              {showTimestamps && line.time && <span style={{ color: "rgba(148,163,184,0.6)", flexShrink: 0 }}>{line.time}</span>}
              <span style={{ color: levelColor[line.level] || "var(--code-text)", flexShrink: 0, width: 46, textTransform: "uppercase", opacity: line.level ? 1 : 0 }}>{line.level && line.level !== "info" ? line.level : ""}</span>
              <span style={{ color: line.level === "error" ? "#fca5a5" : line.level === "warn" || line.level === "warning" ? "#fcd34d" : "var(--code-text)", flex: 1 }}>{line.text}</span>
            </div>
          );
        })}
      </div>
    </div>
  );
}
