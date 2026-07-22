const { IconButton, Tooltip } = window.MendixToolsDesignSystem_3bc077;

const NAV = [
  { id: "environments", label: "Environments", icon: "layout-grid" },
  { id: "backups", label: "Backups", icon: "database-backup" },
  { id: "deploy", label: "Build & Deploy", icon: "rocket" },
  { id: "databases", label: "Local Databases", icon: "database" },
  { id: "settings", label: "Settings", icon: "settings" },
];

function NavItem({ item, active, onClick }) {
  const [hover, setHover] = React.useState(false);
  return (
    <button
      onClick={onClick}
      onMouseEnter={() => setHover(true)}
      onMouseLeave={() => setHover(false)}
      style={{
        display: "flex", alignItems: "center", gap: "11px", width: "100%",
        padding: "8px 10px", border: "none", cursor: "pointer", textAlign: "left",
        borderRadius: "var(--radius-md)", fontFamily: "var(--font-sans)",
        fontSize: "var(--text-base)", fontWeight: active ? "var(--fw-semibold)" : "var(--fw-medium)",
        color: active ? "var(--accent-text)" : "var(--text-secondary)",
        background: active ? "var(--accent-subtle)" : hover ? "var(--bg-hover)" : "transparent",
        transition: "all var(--duration-fast) var(--ease-standard)",
      }}
    >
      <i data-lucide={item.icon} style={{ width: 17, height: 17, flexShrink: 0 }} />
      {item.label}
    </button>
  );
}

function AppShell({ current, onNavigate, theme, onToggleTheme, title, subtitle, actions, children }) {
  return (
    <div style={{ display: "flex", height: "100%", background: "var(--bg-app)", color: "var(--text-primary)" }}>
      {/* Sidebar */}
      <aside style={{
        width: "var(--sidebar-w)", flexShrink: 0, display: "flex", flexDirection: "column",
        borderRight: "var(--border-w) solid var(--border)", background: "var(--bg-surface)",
      }}>
        <div style={{ display: "flex", alignItems: "center", gap: "10px", padding: "14px 16px", height: "var(--topbar-h)", boxSizing: "border-box", borderBottom: "var(--border-w) solid var(--border-subtle)" }}>
          <div style={{ width: 28, height: 28, borderRadius: "7px", background: "var(--accent)", color: "#fff", display: "flex", alignItems: "center", justifyContent: "center", fontFamily: "var(--font-mono)", fontWeight: 600, fontSize: 13, letterSpacing: "-0.03em" }}>mt</div>
          <div style={{ fontSize: "var(--text-md)", fontWeight: "var(--fw-semibold)", letterSpacing: "-0.01em" }}>Mendix <span style={{ color: "var(--text-tertiary)", fontWeight: "var(--fw-medium)" }}>Tools</span></div>
        </div>
        <nav style={{ padding: "10px 10px", display: "flex", flexDirection: "column", gap: "2px", flex: 1 }}>
          <div style={{ fontSize: "var(--text-2xs)", fontWeight: 700, letterSpacing: "0.07em", textTransform: "uppercase", color: "var(--text-tertiary)", padding: "8px 10px 4px" }}>Workspace</div>
          {NAV.map((n) => <NavItem key={n.id} item={n} active={current === n.id} onClick={() => onNavigate(n.id)} />)}
        </nav>
        <div style={{ padding: "10px 12px", borderTop: "var(--border-w) solid var(--border-subtle)", display: "flex", alignItems: "center", gap: "10px" }}>
          <div style={{ width: 30, height: 30, borderRadius: "50%", background: "var(--slate-700)", color: "#fff", display: "flex", alignItems: "center", justifyContent: "center", fontSize: 12, fontWeight: 600, flexShrink: 0 }}>JV</div>
          <div style={{ flex: 1, minWidth: 0 }}>
            <div style={{ fontSize: "var(--text-sm)", fontWeight: "var(--fw-medium)", whiteSpace: "nowrap", overflow: "hidden", textOverflow: "ellipsis" }}>Jarno V.</div>
            <div style={{ fontSize: "var(--text-2xs)", color: "var(--text-tertiary)", fontFamily: "var(--font-mono)" }}>consultant</div>
          </div>
          <Tooltip label={theme === "dark" ? "Light mode" : "Dark mode"}>
            <IconButton icon={theme === "dark" ? "sun" : "moon"} aria-label="Toggle theme" onClick={onToggleTheme} />
          </Tooltip>
        </div>
      </aside>

      {/* Main */}
      <div style={{ flex: 1, display: "flex", flexDirection: "column", minWidth: 0 }}>
        <header style={{
          height: "var(--topbar-h)", flexShrink: 0, display: "flex", alignItems: "center", gap: "16px",
          padding: "0 20px", borderBottom: "var(--border-w) solid var(--border)", background: "var(--bg-surface)",
        }}>
          <div style={{ flex: 1, minWidth: 0 }}>
            <div style={{ fontSize: "var(--text-lg)", fontWeight: "var(--fw-semibold)", letterSpacing: "-0.01em", whiteSpace: "nowrap", overflow: "hidden", textOverflow: "ellipsis" }}>{title}</div>
            {subtitle && <div style={{ fontSize: "var(--text-xs)", color: "var(--text-tertiary)", fontFamily: "var(--font-mono)" }}>{subtitle}</div>}
          </div>
          <div style={{ display: "flex", alignItems: "center", gap: "8px" }}>{actions}</div>
        </header>
        <main style={{ flex: 1, overflow: "auto", padding: "24px" }}>
          <div style={{ maxWidth: "var(--content-max)", margin: "0 auto" }}>{children}</div>
        </main>
      </div>
    </div>
  );
}

Object.assign(window, { AppShell, NAV });
