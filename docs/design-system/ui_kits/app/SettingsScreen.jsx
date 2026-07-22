const { Card, Badge, Button, Input, Select, Switch, Tabs, Radio, StatusDot, Toast, Tag } = window.MendixToolsDesignSystem_3bc077;

function Row({ label, hint, children }) {
  return (
    <div style={{ display: "grid", gridTemplateColumns: "220px 1fr", gap: 20, alignItems: "start", padding: "16px 0", borderBottom: "var(--border-w) solid var(--border-subtle)" }}>
      <div>
        <div style={{ fontSize: "var(--text-base)", fontWeight: "var(--fw-medium)" }}>{label}</div>
        {hint && <div style={{ fontSize: "var(--text-xs)", color: "var(--text-tertiary)", marginTop: 3, lineHeight: 1.45 }}>{hint}</div>}
      </div>
      <div>{children}</div>
    </div>
  );
}

function SettingsScreen({ theme, onToggleTheme }) {
  const [tab, setTab] = React.useState("credentials");
  const [tested, setTested] = React.useState(false);

  return (
    <div style={{ display: "flex", flexDirection: "column", gap: "18px", maxWidth: 860 }}>
      <Tabs value={tab} onChange={setTab} tabs={[
        { value: "credentials", label: "Credentials", icon: <i data-lucide="key-round" style={{ width: 15, height: 15 }} /> },
        { value: "database", label: "Database", icon: <i data-lucide="database" style={{ width: 15, height: 15 }} /> },
        { value: "preferences", label: "Preferences", icon: <i data-lucide="sliders-horizontal" style={{ width: 15, height: 15 }} /> },
      ]} />

      {tab === "credentials" && (
        <Card title="Mendix platform" subtitle="Personal Access Token used to list environments and pull backups"
          actions={<Badge tone="success" dot>Connected</Badge>}>
          <div>
            <Row label="Personal Access Token" hint="Scopes: mx:deploy, mx:backups. Stored in the OS credential vault.">
              <Input mono type="password" defaultValue="mxpat_9f3a2c7e10b44d8e" rightSlot={<i data-lucide="eye" style={{ width: 15, height: 15, color: "var(--text-tertiary)", cursor: "pointer" }} />} />
            </Row>
            <Row label="Default project" hint="Applied when opening the Environments view.">
              <Select options={[{ value: "acme", label: "Acme Insurance" }, { value: "belfort", label: "Belfort Logistics" }, { value: "kwik", label: "KwikPark" }]} />
            </Row>
            <Row label="API region" hint="Endpoint for deploy & backup calls.">
              <div style={{ display: "flex", gap: 8 }}>
                <Tag tone="accent">eu-west-1</Tag><Tag>us-east-1</Tag>
              </div>
            </Row>
            <div style={{ display: "flex", justifyContent: "flex-end", gap: 8, paddingTop: 16 }}>
              <Button variant="secondary">Revoke</Button>
              <Button>Save</Button>
            </div>
          </div>
        </Card>
      )}

      {tab === "database" && (
        <Card title="Local PostgreSQL" subtitle="Connection used for restores and dumps"
          actions={<StatusDot status="running" pulse label="Reachable" />}>
          <div>
            <Row label="Host & port">
              <div style={{ display: "flex", gap: 10 }}>
                <Input mono defaultValue="localhost" containerStyle={{ flex: 1 }} leftIcon={<i data-lucide="server" style={{ width: 15, height: 15 }} />} />
                <Input mono defaultValue="5432" containerStyle={{ width: 100 }} />
              </div>
            </Row>
            <Row label="Credentials">
              <div style={{ display: "flex", gap: 10 }}>
                <Input mono defaultValue="mendix" containerStyle={{ flex: 1 }} />
                <Input mono type="password" defaultValue="postgres" containerStyle={{ flex: 1 }} />
              </div>
            </Row>
            <Row label="Data directory" hint="Where restored .backup files and dumps are written.">
              <Input mono defaultValue="C:\\MendixTools\\data" leftIcon={<i data-lucide="folder" style={{ width: 15, height: 15 }} />} />
            </Row>
            <div style={{ display: "flex", alignItems: "center", justifyContent: "flex-end", gap: 10, paddingTop: 16 }}>
              {tested && <span style={{ display: "inline-flex", alignItems: "center", gap: 6, fontSize: "var(--text-sm)", color: "var(--success-text)" }}><i data-lucide="check-circle-2" style={{ width: 15, height: 15 }} />Connection OK · 6ms</span>}
              <Button variant="secondary" leftIcon={<i data-lucide="plug" style={{ width: 15, height: 15 }} />} onClick={() => setTested(true)}>Test connection</Button>
              <Button>Save</Button>
            </div>
          </div>
        </Card>
      )}

      {tab === "preferences" && (
        <Card title="Preferences">
          <div>
            <Row label="Theme" hint="Also toggled from the sidebar.">
              <div style={{ display: "flex", flexDirection: "column", gap: 10 }}>
                <Radio name="th" label="Light" checked={theme !== "dark"} onChange={() => theme === "dark" && onToggleTheme()} />
                <Radio name="th" label="Dark" checked={theme === "dark"} onChange={() => theme !== "dark" && onToggleTheme()} />
              </div>
            </Row>
            <Row label="Auto-refresh" hint="Poll environment status in the background.">
              <Switch label="Every 30 seconds" defaultChecked />
            </Row>
            <Row label="Backups" hint="Behaviour after a restore completes.">
              <div style={{ display: "flex", flexDirection: "column", gap: 12 }}>
                <Switch label="Keep downloaded .backup files" defaultChecked />
                <Switch label="Verify checksum after download" defaultChecked />
              </div>
            </Row>
            <div style={{ display: "flex", justifyContent: "flex-end", paddingTop: 16 }}>
              <Button>Save preferences</Button>
            </div>
          </div>
        </Card>
      )}
    </div>
  );
}

Object.assign(window, { SettingsScreen });
