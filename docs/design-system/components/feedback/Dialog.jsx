import React from "react";

/**
 * Modal dialog with overlay, header, body, and footer actions. Controlled via
 * `open`/`onClose`. Used for confirmations (restore, delete env) and short forms.
 */
export function Dialog({ open, onClose, title, description, children, footer, width = 460, tone, icon, ...rest }) {
  React.useEffect(() => {
    if (!open) return;
    const onKey = (e) => e.key === "Escape" && onClose?.();
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [open, onClose]);
  if (!open) return null;

  const toneColor = tone === "danger" ? "var(--danger)" : tone === "warning" ? "var(--warning)" : "var(--accent)";
  const toneBg = tone === "danger" ? "var(--danger-subtle)" : tone === "warning" ? "var(--warning-subtle)" : "var(--accent-subtle)";

  return (
    <div
      onClick={onClose}
      style={{
        position: "fixed", inset: 0, zIndex: "var(--z-modal)", background: "var(--overlay)",
        display: "flex", alignItems: "center", justifyContent: "center", padding: "24px",
        backdropFilter: "blur(2px)", animation: "mxt-fade var(--duration-normal) var(--ease-out)",
      }}
    >
      <div
        role="dialog" aria-modal="true" onClick={(e) => e.stopPropagation()}
        style={{
          width, maxWidth: "100%", maxHeight: "90vh", overflow: "auto",
          background: "var(--bg-surface-raised)", borderRadius: "var(--radius-2xl)",
          border: "var(--border-w) solid var(--border)", boxShadow: "var(--shadow-xl)",
          animation: "mxt-pop var(--duration-normal) var(--ease-out)",
        }}
        {...rest}
      >
        <div style={{ display: "flex", gap: "14px", padding: "20px 20px 0" }}>
          {icon && (
            <div style={{
              flexShrink: 0, width: 38, height: 38, borderRadius: "var(--radius-lg)",
              background: toneBg, color: toneColor, display: "flex", alignItems: "center", justifyContent: "center",
            }}>{icon}</div>
          )}
          <div style={{ flex: 1, minWidth: 0 }}>
            {title && <h3 style={{ fontSize: "var(--text-xl)", fontWeight: "var(--fw-semibold)", color: "var(--text-primary)" }}>{title}</h3>}
            {description && <p style={{ marginTop: "6px", fontSize: "var(--text-base)", color: "var(--text-secondary)", lineHeight: 1.5 }}>{description}</p>}
          </div>
          <button type="button" onClick={onClose} aria-label="Close" style={{
            flexShrink: 0, width: 30, height: 30, border: "none", background: "transparent",
            color: "var(--text-tertiary)", cursor: "pointer", borderRadius: "var(--radius-md)",
            display: "flex", alignItems: "center", justifyContent: "center",
          }}><i data-lucide="x" style={{ width: 18, height: 18 }} /></button>
        </div>
        {children && <div style={{ padding: "16px 20px" }}>{children}</div>}
        {footer && (
          <div style={{
            display: "flex", justifyContent: "flex-end", gap: "8px",
            padding: "14px 20px", borderTop: "var(--border-w) solid var(--border-subtle)", marginTop: children ? 0 : "16px",
          }}>{footer}</div>
        )}
        <style>{"@keyframes mxt-fade{from{opacity:0}}@keyframes mxt-pop{from{opacity:0;transform:translateY(8px) scale(0.98)}}"}</style>
      </div>
    </div>
  );
}
