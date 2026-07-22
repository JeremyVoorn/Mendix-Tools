const { Card, Badge, StatusDot, Button, IconButton, Tag } = window.MendixToolsDesignSystem_3bc077;

const ENVS = [
  { id: "acme-prod", app: "Acme Insurance", env: "Production", tone: "success", status: "Running", statusKey: "running", version: "10.12.4", db: "2.4 GB", region: "eu-west-1", host: "acme-prod.mendixcloud.com", lastBackup: "2h ago" },
  { id: "acme-accp", app: "Acme Insurance", env: "Acceptance", tone: "success", status: "Running", statusKey: "running", version: "10.12.4", db: "1.9 GB", region: "eu-west-1", host: "acme-accp.mendixcloud.com", lastBackup: "1d ago" },
  { id: "acme-test", app: "Acme Insurance", env: "Test", tone: "danger", status: "Stopped", statusKey: "stopped", version: "10.11.0", db: "820 MB", region: "eu-west-1", host: "acme-test.mendixcloud.com", lastBackup: "6d ago" },
  { id: "belfort-prod", app: "Belfort Logistics", env: "Production", tone: "warning", status: "Degraded", statusKey: "warning", version: "9.24.2", db: "5.1 GB", region: "eu-central-1", host: "belfort.mendixcloud.com", lastBackup: "4h ago" },
  { id: "kwik-accp", app: "KwikPark", env: "Acceptance", tone: "success", status: "Running", statusKey: "running", version: "10.6.1", db: "640 MB", region: "us-east-1", host: "kwikpark-accp.mendixcloud.com", lastBackup: "3d ago" },
  { id: "kwik-prod", app: "KwikPark", env: "Production", tone: "accent", status: "Deploying", statusKey: "deploying", version: "10.6.1", db: "1.2 GB", region: "us-east-1", host: "kwikpark.mendixcloud.com", lastBackup: "just now" },
];

function Stat({ icon, label, value, sub, tone }) {
  return (
    <Card padding="15px 16px" style={{ flex: 1 }}>
      <div style={{ display: "flex", alignItems: "center", gap: "10px" }}>
        <div style={{ width: 34, height: 34, borderRadius: "var(--radius-lg)", background: `var(--${tone}-subtle)`, color: `var(--${tone}${tone === "accent" ? "-text" : ""})`, display: "flex", alignItems: "center", justifyContent: "center", flexShrink: 0 }}>
          <i data-lucide={icon} style={{ width: 17, height: 17 }} />
        </div>
        <div>
          <div style={{ fontFamily: "var(--font-mono)", fontSize: "var(--text-2xl)", fontWeight: 500, lineHeight: 1, letterSpacing: "-0.02em" }}>{value}</div>
          <div style={{ fontSize: "var(--text-xs)", color: "var(--text-tertiary)", marginTop: 4 }}>{label}{sub && <span> · {sub}</span>}</div>
        </div>
      </div>
    </Card>
  );
}

function EnvCard({ e, onOpen }) {
  return (
    <Card interactive onClick={() => onOpen(e)} padding="0" style={{ display: "flex", flexDirection: "column" }}>
      <div style={{ padding: "14px 15px", display: "flex", alignItems: "flex-start", justifyContent: "space-between", gap: 10 }}>
        <div style={{ minWidth: 0 }}>
          <div style={{ fontSize: "var(--text-md)", fontWeight: "var(--fw-semibold)" }}>{e.app}</div>
          <div style={{ display: "flex", alignItems: "center", gap: 6, marginTop: 3 }}>
            <Tag>{e.env}</Tag>
            <span style={{ fontFamily: "var(--font-mono)", fontSize: "var(--text-2xs)", color: "var(--text-tertiary)" }}>{e.region}</span>
          </div>
        </div>
        <Badge tone={e.tone} dot>{e.status === "Deploying" ? "Deploying" : e.status}</Badge>
      </div>
      <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr 1fr", borderTop: "var(--border-w) solid var(--border-subtle)" }}>
        {[["Mendix", e.version], ["DB size", e.db], ["Backup", e.lastBackup]].map(([k, v], i) => (
          <div key={k} style={{ padding: "10px 15px", borderLeft: i ? "var(--border-w) solid var(--border-subtle)" : "none" }}>
            <div style={{ fontSize: "var(--text-2xs)", color: "var(--text-tertiary)", textTransform: "uppercase", letterSpacing: "0.05em" }}>{k}</div>
            <div style={{ fontFamily: "var(--font-mono)", fontSize: "var(--text-sm)", marginTop: 3 }}>{v}</div>
          </div>
        ))}
      </div>
      <div style={{ display: "flex", gap: 6, padding: "10px 15px", borderTop: "var(--border-w) solid var(--border-subtle)" }} onClick={(ev) => ev.stopPropagation()}>
        <Button size="sm" variant="secondary" leftIcon={<i data-lucide="download" style={{ width: 14, height: 14 }} />}>Backup</Button>
        <Button size="sm" variant="ghost" leftIcon={<i data-lucide="external-link" style={{ width: 14, height: 14 }} />}>Open</Button>
        <div style={{ marginLeft: "auto" }}><IconButton size="sm" icon="more-horizontal" aria-label="More" /></div>
      </div>
    </Card>
  );
}

function EnvironmentsScreen({ onOpen }) {
  return (
    <div style={{ display: "flex", flexDirection: "column", gap: "20px" }}>
      <div style={{ display: "flex", gap: "14px" }}>
        <Stat icon="layout-grid" label="Environments" value="6" tone="accent" />
        <Stat icon="check-circle-2" label="Running" value="4" tone="success" />
        <Stat icon="database" label="Local DBs" value="3" tone="db" />
        <Stat icon="hard-drive" label="Storage used" value="12 GB" sub="local" tone="package" />
      </div>

      <div>
        <div style={{ display: "flex", alignItems: "center", justifyContent: "space-between", marginBottom: 12 }}>
          <div style={{ fontSize: "var(--text-md)", fontWeight: "var(--fw-semibold)" }}>All environments</div>
          <div style={{ display: "flex", gap: 6 }}>
            <Tag tone="accent" icon={<i data-lucide="filter" style={{ width: 12, height: 12 }} />}>3 apps</Tag>
          </div>
        </div>
        <div style={{ display: "grid", gridTemplateColumns: "repeat(auto-fill, minmax(320px, 1fr))", gap: "14px" }}>
          {ENVS.map((e) => <EnvCard key={e.id} e={e} onOpen={onOpen} />)}
        </div>
      </div>
    </div>
  );
}

Object.assign(window, { EnvironmentsScreen, ENVS });
