const { Card, Badge, Button, IconButton, DataTable, StatusDot, Tag, Tooltip, ProgressBar } = window.MendixToolsDesignSystem_3bc077;

const DBS = [
  { id: "acme_local", name: "acme_local", src: "Acme · Production", size: "2.41 GB", created: "2h ago", mx: "10.12.4" },
  { id: "acme_accp", name: "acme_accp_local", src: "Acme · Acceptance", size: "1.90 GB", created: "1d ago", mx: "10.12.4" },
  { id: "belfort", name: "belfort_local", src: "Belfort · Production", size: "5.08 GB", created: "4h ago", mx: "9.24.2" },
  { id: "kwik", name: "kwikpark_local", src: "KwikPark · Acceptance", size: "0.64 GB", created: "3d ago", mx: "10.6.1" },
];

function DatabasesScreen() {
  return (
    <div style={{ display: "flex", flexDirection: "column", gap: "18px" }}>
      {/* Server status */}
      <Card padding="0">
        <div style={{ display: "flex", alignItems: "center", gap: 16, padding: "16px 18px" }}>
          <div style={{ width: 44, height: 44, borderRadius: "var(--radius-lg)", background: "var(--db-subtle)", color: "var(--db-text)", display: "flex", alignItems: "center", justifyContent: "center", flexShrink: 0 }}>
            <i data-lucide="database" style={{ width: 22, height: 22 }} />
          </div>
          <div style={{ flex: 1, minWidth: 0 }}>
            <div style={{ display: "flex", alignItems: "center", gap: 10 }}>
              <span style={{ fontSize: "var(--text-lg)", fontWeight: "var(--fw-semibold)" }}>Local PostgreSQL</span>
              <Badge tone="db">16.2</Badge>
              <StatusDot status="running" pulse label="Running" />
            </div>
            <div style={{ fontFamily: "var(--font-mono)", fontSize: "var(--text-xs)", color: "var(--text-tertiary)", marginTop: 3 }}>Host=localhost;Port=5432;Username=mendix · uptime 4d 6h</div>
          </div>
          <div style={{ display: "flex", gap: 8 }}>
            <Button variant="secondary" size="sm" leftIcon={<i data-lucide="square" style={{ width: 13, height: 13 }} />}>Stop</Button>
            <Button variant="secondary" size="sm" leftIcon={<i data-lucide="rotate-cw" style={{ width: 13, height: 13 }} />}>Restart</Button>
            <Tooltip label="Open in pgAdmin"><IconButton variant="secondary" icon="external-link" aria-label="Open in pgAdmin" /></Tooltip>
          </div>
        </div>
        <div style={{ display: "grid", gridTemplateColumns: "repeat(4,1fr)", borderTop: "var(--border-w) solid var(--border-subtle)" }}>
          {[["Databases", "4"], ["Total size", "10.03 GB"], ["Disk free", "182 GB"], ["Connections", "3 / 100"]].map(([k, v], i) => (
            <div key={k} style={{ padding: "12px 18px", borderLeft: i ? "var(--border-w) solid var(--border-subtle)" : "none" }}>
              <div style={{ fontSize: "var(--text-2xs)", color: "var(--text-tertiary)", textTransform: "uppercase", letterSpacing: "0.05em" }}>{k}</div>
              <div style={{ fontFamily: "var(--font-mono)", fontSize: "var(--text-lg)", marginTop: 3 }}>{v}</div>
            </div>
          ))}
        </div>
      </Card>

      {/* Disk usage bar */}
      <Card padding="15px 16px">
        <ProgressBar label="Local storage — 10.03 GB of 192 GB used by Mendix Tools" value={10.03} max={192} tone="db" />
      </Card>

      {/* DB list */}
      <Card padding="0" title="Local databases" subtitle="Restored snapshots on your machine"
        actions={<Button size="sm" variant="secondary" leftIcon={<i data-lucide="plus" style={{ width: 14, height: 14 }} />}>New database</Button>}>
        <DataTable
          columns={[
            { key: "name", header: "Database", mono: true, render: (v) => <span style={{ display: "inline-flex", alignItems: "center", gap: 7 }}><i data-lucide="database" style={{ width: 14, height: 14, color: "var(--db)" }} />{v}</span> },
            { key: "src", header: "Source", render: (v) => <Tag>{v}</Tag> },
            { key: "mx", header: "Mendix", mono: true },
            { key: "size", header: "Size", mono: true, align: "right" },
            { key: "created", header: "Restored", align: "right" },
            { key: "actions", header: "", align: "right", width: "150px", render: () => (
              <div style={{ display: "flex", gap: 6, justifyContent: "flex-end" }} onClick={(e) => e.stopPropagation()}>
                <Tooltip label="Connect"><IconButton size="sm" icon="plug" aria-label="Connect" /></Tooltip>
                <Tooltip label="Dump to file"><IconButton size="sm" icon="file-down" aria-label="Dump" /></Tooltip>
                <Tooltip label="Drop database"><IconButton size="sm" icon="trash-2" variant="danger" aria-label="Drop" /></Tooltip>
              </div>
            ) },
          ]}
          rows={DBS}
        />
      </Card>
    </div>
  );
}

Object.assign(window, { DatabasesScreen });
