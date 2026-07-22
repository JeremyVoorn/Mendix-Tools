const { Card, Badge, Button, IconButton, Select, Input, LogViewer, ProgressBar, DataTable, Tag, Switch, StatusDot } = window.MendixToolsDesignSystem_3bc077;

const HISTORY = [
  { id: "d1", when: "2026-07-22 09:40", env: "Production", version: "10.12.4", by: "Jarno V.", state: "Succeeded" },
  { id: "d2", when: "2026-07-21 14:02", env: "Acceptance", version: "10.12.4", by: "Jarno V.", state: "Succeeded" },
  { id: "d3", when: "2026-07-21 11:20", env: "Acceptance", version: "10.12.3", by: "Sam D.", state: "Failed" },
  { id: "d4", when: "2026-07-19 16:55", env: "Test", version: "10.12.1", by: "Jarno V.", state: "Succeeded" },
];

const SCRIPT = [
  { level: "cmd", text: "$ mx build --project AcmeInsurance.mpr --output acme-10.12.4.mpk" },
  { text: "Resolving module dependencies…" },
  { text: "Compiling Java actions (48 files)" },
  { level: "success", text: "Build succeeded — acme-10.12.4.mpk (48.2 MB)" },
  { level: "cmd", text: "$ mx deploy --env prod --package acme-10.12.4.mpk" },
  { text: "Uploading package to acme-prod.mendixcloud.com…" },
  { level: "warn", text: "Constant 'FeatureFlags.NewUI' not set — using default (false)" },
  { text: "Transporting package… stopping app" },
  { text: "Applying database migrations (3 pending)" },
  { text: "Starting app…" },
  { level: "success", text: "Deployment finished in 1m 12s · health check OK" },
];

function DeployScreen() {
  const [lines, setLines] = React.useState([{ time: "—", level: "info", text: "Ready. Configure a build and deploy." }]);
  const [running, setRunning] = React.useState(false);
  const [pct, setPct] = React.useState(0);
  const timer = React.useRef(null);

  const run = () => {
    if (running) return;
    setRunning(true); setPct(0); setLines([]);
    let i = 0;
    clearInterval(timer.current);
    timer.current = setInterval(() => {
      const now = new Date();
      const t = now.toTimeString().slice(0, 8);
      setLines((prev) => [...prev, { ...SCRIPT[i], time: t }]);
      setPct(Math.round(((i + 1) / SCRIPT.length) * 100));
      i++;
      if (i >= SCRIPT.length) { clearInterval(timer.current); setRunning(false); }
    }, 650);
  };
  React.useEffect(() => () => clearInterval(timer.current), []);

  return (
    <div style={{ display: "flex", flexDirection: "column", gap: "18px" }}>
      <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: "14px" }}>
        <Card title="Package" subtitle="Build from local project" actions={<Badge tone="package">mpk</Badge>}>
          <div style={{ display: "flex", flexDirection: "column", gap: 12 }}>
            <Input label="Project file" mono defaultValue="AcmeInsurance.mpr" leftIcon={<i data-lucide="folder" style={{ width: 15, height: 15 }} />} />
            <div style={{ display: "flex", gap: 10 }}>
              <Select label="Branch" containerStyle={{ flex: 1 }} options={[{ value: "main", label: "main" }, { value: "rc", label: "release/10.12" }]} />
              <Input label="Version" mono defaultValue="10.12.4" containerStyle={{ width: 110 }} />
            </div>
            <div style={{ display: "flex", alignItems: "center", gap: 8, fontSize: "var(--text-sm)", color: "var(--text-secondary)" }}>
              <i data-lucide="git-branch" style={{ width: 14, height: 14 }} />
              <span style={{ fontFamily: "var(--font-mono)" }}>a1f9c2e</span> · 4 commits ahead
            </div>
          </div>
        </Card>

        <Card title="Target" subtitle="Where to deploy" actions={<StatusDot status="running" pulse label="Production" />}>
          <div style={{ display: "flex", flexDirection: "column", gap: 12 }}>
            <Select label="Environment" options={[{ value: "prod", label: "Acme Insurance · Production" }, { value: "accp", label: "Acme Insurance · Acceptance" }]} />
            <Switch label="Backup before deploy" defaultChecked />
            <Switch label="Run database migrations" defaultChecked />
            <Switch label="Send deploy notification to Slack" />
          </div>
        </Card>
      </div>

      <Card padding="0"
        title="Build & deploy output"
        actions={<>
          {running ? <div style={{ width: 160 }}><ProgressBar value={pct} tone="package" showValue /></div>
            : <Button leftIcon={<i data-lucide="rocket" style={{ width: 15, height: 15 }} />} onClick={run}>Build & deploy</Button>}
          <IconButton icon="download" aria-label="Download log" />
        </>}>
        <div style={{ padding: 14 }}>
          <LogViewer height={230} lines={lines} follow />
        </div>
      </Card>

      <Card padding="0" title="Recent deployments">
        <DataTable
          columns={[
            { key: "when", header: "When", mono: true },
            { key: "env", header: "Environment", render: (v) => <Tag>{v}</Tag> },
            { key: "version", header: "Version", mono: true },
            { key: "by", header: "By" },
            { key: "state", header: "Result", align: "right", render: (v) => <Badge tone={v === "Succeeded" ? "success" : "danger"} dot>{v}</Badge> },
          ]}
          rows={HISTORY}
        />
      </Card>
    </div>
  );
}

Object.assign(window, { DeployScreen });
