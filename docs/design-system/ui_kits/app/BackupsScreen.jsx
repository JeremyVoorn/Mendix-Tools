const { Card, Badge, Button, IconButton, DataTable, Select, ProgressBar, Dialog, Checkbox, Radio, Input, StatusDot, Tag } = window.MendixToolsDesignSystem_3bc077;

const BACKUPS = [
  { id: "b1", created: "2026-07-22 09:12", env: "Production", type: "Automatic", size: "2.41 GB", state: "Available" },
  { id: "b2", created: "2026-07-21 09:12", env: "Production", type: "Automatic", size: "2.38 GB", state: "Available" },
  { id: "b3", created: "2026-07-20 16:44", env: "Production", type: "Manual", size: "2.39 GB", state: "Available" },
  { id: "b4", created: "2026-07-20 09:12", env: "Acceptance", type: "Automatic", size: "1.90 GB", state: "Available" },
  { id: "b5", created: "2026-07-19 09:12", env: "Production", type: "Automatic", size: "2.35 GB", state: "Archived" },
];

function BackupsScreen({ onRunJob }) {
  const [env, setEnv] = React.useState("prod");
  const [sel, setSel] = React.useState([]);
  const [dialog, setDialog] = React.useState(null); // backup being restored
  const [strategy, setStrategy] = React.useState("clean");
  const [job, setJob] = React.useState(null);
  const timer = React.useRef(null);

  const startRestore = () => {
    const b = dialog; setDialog(null);
    setJob({ backup: b, phase: "Downloading backup", pct: 0, tone: "db" });
    let pct = 0;
    clearInterval(timer.current);
    timer.current = setInterval(() => {
      pct += Math.random() * 14 + 6;
      if (pct >= 100) {
        pct = 100; clearInterval(timer.current);
        setJob((j) => ({ ...j, phase: "Restore complete", pct: 100, tone: "success", done: true }));
        onRunJob && onRunJob(b);
      } else {
        const phase = pct < 55 ? "Downloading backup" : pct < 80 ? "Dropping & recreating schema" : "Importing into acme_local";
        setJob((j) => ({ ...j, phase, pct, tone: pct < 55 ? "db" : "accent" }));
      }
    }, 550);
  };

  React.useEffect(() => () => clearInterval(timer.current), []);

  return (
    <div style={{ display: "flex", flexDirection: "column", gap: "18px" }}>
      {/* Controls */}
      <div style={{ display: "flex", alignItems: "flex-end", gap: "12px", flexWrap: "wrap" }}>
        <Select label="Source environment" value={env} onChange={(e) => setEnv(e.target.value)} containerStyle={{ width: 240 }}
          options={[{ value: "prod", label: "Acme Insurance · Production" }, { value: "accp", label: "Acme Insurance · Acceptance" }, { value: "test", label: "Acme Insurance · Test" }]} />
        <div style={{ flex: 1 }} />
        <Button variant="secondary" leftIcon={<i data-lucide="refresh-cw" style={{ width: 15, height: 15 }} />}>Refresh</Button>
        <Button leftIcon={<i data-lucide="plus" style={{ width: 15, height: 15 }} />}>Create backup</Button>
      </div>

      {/* Active job */}
      {job && (
        <Card padding="15px 16px" style={{ borderColor: job.done ? "var(--success)" : "var(--accent-subtle-border)" }}>
          <div style={{ display: "flex", alignItems: "center", gap: 12 }}>
            <StatusDot status={job.done ? "success" : "deploying"} pulse={!job.done} size={9} />
            <div style={{ flex: 1 }}>
              <div style={{ display: "flex", justifyContent: "space-between", marginBottom: 8 }}>
                <span style={{ fontSize: "var(--text-sm)", fontWeight: "var(--fw-medium)" }}>{job.phase} · <span style={{ fontFamily: "var(--font-mono)", color: "var(--text-tertiary)" }}>{job.backup.created}</span></span>
                <span style={{ fontFamily: "var(--font-mono)", fontSize: "var(--text-xs)", color: "var(--text-tertiary)" }}>{Math.round(job.pct)}%</span>
              </div>
              <ProgressBar value={job.pct} tone={job.tone} showValue={false} />
            </div>
            {job.done && <Button size="sm" variant="secondary" onClick={() => setJob(null)}>Dismiss</Button>}
          </div>
        </Card>
      )}

      {/* Backup list */}
      <Card padding="0" title="Cloud backups" subtitle="Restore any snapshot into your local Postgres server"
        actions={sel.length > 0 && <Button size="sm" variant="secondary" leftIcon={<i data-lucide="download" style={{ width: 14, height: 14 }} />}>Download {sel.length}</Button>}>
        <DataTable
          selectable selected={sel} onSelectChange={setSel}
          columns={[
            { key: "created", header: "Created", mono: true },
            { key: "env", header: "Environment", render: (v) => <Tag>{v}</Tag> },
            { key: "type", header: "Type" },
            { key: "size", header: "Size", mono: true, align: "right" },
            { key: "state", header: "State", render: (v) => <Badge tone={v === "Available" ? "success" : "neutral"} dot={v === "Available"}>{v}</Badge> },
            { key: "actions", header: "", align: "right", width: "180px", render: (_, r) => (
              <div style={{ display: "flex", gap: 6, justifyContent: "flex-end" }} onClick={(e) => e.stopPropagation()}>
                <Button size="sm" variant="subtle" leftIcon={<i data-lucide="database-backup" style={{ width: 14, height: 14 }} />} onClick={() => setDialog(r)}>Restore</Button>
                <IconButton size="sm" icon="download" aria-label="Download" />
              </div>
            ) },
          ]}
          rows={BACKUPS}
        />
      </Card>

      {/* Restore dialog */}
      <Dialog open={!!dialog} onClose={() => setDialog(null)} tone="warning" width={480}
        icon={<i data-lucide="database-backup" style={{ width: 20, height: 20 }} />}
        title="Restore to local Postgres"
        description={dialog ? `Snapshot from ${dialog.created} (${dialog.size}) will be imported into your local server.` : ""}
        footer={<><Button variant="secondary" onClick={() => setDialog(null)}>Cancel</Button><Button onClick={startRestore} leftIcon={<i data-lucide="play" style={{ width: 15, height: 15 }} />}>Start restore</Button></>}>
        <div style={{ display: "flex", flexDirection: "column", gap: 14 }}>
          <Input label="Target database" mono defaultValue="acme_local" leftIcon={<i data-lucide="database" style={{ width: 15, height: 15 }} />} />
          <div>
            <div style={{ fontSize: "var(--text-sm)", fontWeight: "var(--fw-medium)", color: "var(--text-secondary)", marginBottom: 8 }}>Restore strategy</div>
            <div style={{ display: "flex", flexDirection: "column", gap: 10 }}>
              <Radio name="strat" value="clean" checked={strategy === "clean"} onChange={() => setStrategy("clean")} label="Clean restore" description="Drop and recreate the schema before import" />
              <Radio name="strat" value="merge" checked={strategy === "merge"} onChange={() => setStrategy("merge")} label="Merge into existing" description="Keep current data, import over it" />
            </div>
          </div>
          <Checkbox label="Keep the downloaded .backup file after restore" defaultChecked />
        </div>
      </Dialog>
    </div>
  );
}

Object.assign(window, { BackupsScreen });
