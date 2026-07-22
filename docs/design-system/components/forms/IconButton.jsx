import React from "react";

const sizeMap = { sm: 28, md: 34, lg: 40 };
const iconSize = { sm: 15, md: 17, lg: 19 };

/**
 * Square icon-only button. Used across toolbars: refresh, more-actions,
 * theme toggle, open in browser, etc.
 */
export function IconButton({
  icon,
  variant = "ghost",
  size = "md",
  disabled = false,
  "aria-label": ariaLabel,
  onClick,
  style,
  ...rest
}) {
  const dim = sizeMap[size] || sizeMap.md;
  const [hover, setHover] = React.useState(false);
  const [active, setActive] = React.useState(false);

  const variants = {
    ghost: { bg: "transparent", color: "var(--text-secondary)", border: "transparent", hoverBg: "var(--bg-hover)" },
    secondary: { bg: "var(--bg-surface)", color: "var(--text-primary)", border: "var(--border-strong)", hoverBg: "var(--bg-subtle)" },
    primary: { bg: "var(--accent)", color: "var(--on-accent)", border: "transparent", hoverBg: "var(--accent-hover)" },
    danger: { bg: "transparent", color: "var(--danger)", border: "transparent", hoverBg: "var(--danger-subtle)" },
  };
  const v = variants[variant] || variants.ghost;

  return (
    <button
      type="button"
      aria-label={ariaLabel}
      disabled={disabled}
      onClick={onClick}
      onMouseEnter={() => setHover(true)}
      onMouseLeave={() => { setHover(false); setActive(false); }}
      onMouseDown={() => setActive(true)}
      onMouseUp={() => setActive(false)}
      style={{
        display: "inline-flex", alignItems: "center", justifyContent: "center",
        width: dim, height: dim, padding: 0,
        background: hover && !disabled ? v.hoverBg : v.bg,
        color: v.color,
        border: `var(--border-w) solid ${v.border}`,
        borderRadius: "var(--radius-md)",
        cursor: disabled ? "not-allowed" : "pointer",
        opacity: disabled ? 0.45 : 1,
        transition: "background var(--duration-fast) var(--ease-standard), transform var(--duration-fast) var(--ease-standard)",
        transform: active && !disabled ? "scale(0.94)" : "none",
        ...style,
      }}
      {...rest}
    >
      {typeof icon === "string"
        ? <i data-lucide={icon} style={{ width: iconSize[size], height: iconSize[size] }} />
        : icon}
    </button>
  );
}
