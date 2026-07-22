import React from "react";

const sizeMap = {
  sm: { height: "var(--control-sm)", padding: "0 10px", font: "var(--text-sm)", gap: "6px" },
  md: { height: "var(--control-md)", padding: "0 14px", font: "var(--text-base)", gap: "8px" },
  lg: { height: "var(--control-lg)", padding: "0 18px", font: "var(--text-md)", gap: "8px" },
};

function variantStyle(variant) {
  switch (variant) {
    case "secondary":
      return { background: "var(--bg-surface)", color: "var(--text-primary)", border: "var(--border-w) solid var(--border-strong)", boxShadow: "var(--shadow-xs)" };
    case "ghost":
      return { background: "transparent", color: "var(--text-secondary)", border: "var(--border-w) solid transparent" };
    case "danger":
      return { background: "var(--danger)", color: "#fff", border: "var(--border-w) solid transparent", boxShadow: "var(--shadow-xs)" };
    case "subtle":
      return { background: "var(--accent-subtle)", color: "var(--accent-text)", border: "var(--border-w) solid transparent" };
    case "primary":
    default:
      return { background: "var(--accent)", color: "var(--on-accent)", border: "var(--border-w) solid transparent", boxShadow: "var(--shadow-xs)" };
  }
}

/**
 * Primary action button for Mendix Tools. Humanist-sans label, 8px radius,
 * subtle lift shadow on solid variants.
 */
export function Button({
  children,
  variant = "primary",
  size = "md",
  leftIcon,
  rightIcon,
  loading = false,
  disabled = false,
  fullWidth = false,
  type = "button",
  onClick,
  style,
  ...rest
}) {
  const s = sizeMap[size] || sizeMap.md;
  const isDisabled = disabled || loading;
  const [hover, setHover] = React.useState(false);
  const [active, setActive] = React.useState(false);
  const base = variantStyle(variant);

  const hoverBg = {
    primary: "var(--accent-hover)",
    danger: "var(--red-700)",
    secondary: "var(--bg-subtle)",
    ghost: "var(--bg-hover)",
    subtle: "var(--accent-subtle)",
  }[variant];

  return (
    <button
      type={type}
      disabled={isDisabled}
      onClick={onClick}
      onMouseEnter={() => setHover(true)}
      onMouseLeave={() => { setHover(false); setActive(false); }}
      onMouseDown={() => setActive(true)}
      onMouseUp={() => setActive(false)}
      style={{
        display: "inline-flex", alignItems: "center", justifyContent: "center",
        gap: s.gap, height: s.height, padding: s.padding,
        width: fullWidth ? "100%" : undefined,
        font: "inherit", fontSize: s.font, fontWeight: "var(--fw-medium)",
        fontFamily: "var(--font-sans)", lineHeight: 1,
        borderRadius: "var(--radius-lg)", cursor: isDisabled ? "not-allowed" : "pointer",
        opacity: isDisabled ? 0.5 : 1,
        transition: "background var(--duration-fast) var(--ease-standard), transform var(--duration-fast) var(--ease-standard), box-shadow var(--duration-fast) var(--ease-standard)",
        transform: active && !isDisabled ? "translateY(0.5px) scale(0.99)" : "none",
        whiteSpace: "nowrap", userSelect: "none",
        ...base,
        ...(hover && !isDisabled ? { background: hoverBg } : null),
        ...style,
      }}
      {...rest}
    >
      {loading && <Spinner />}
      {!loading && leftIcon}
      {children != null && <span>{children}</span>}
      {!loading && rightIcon}
    </button>
  );
}

function Spinner() {
  return (
    <span
      style={{
        width: "14px", height: "14px", borderRadius: "50%",
        border: "2px solid currentColor", borderTopColor: "transparent",
        display: "inline-block", animation: "mxt-spin 0.7s linear infinite",
      }}
    >
      <style>{"@keyframes mxt-spin{to{transform:rotate(360deg)}}"}</style>
    </span>
  );
}
