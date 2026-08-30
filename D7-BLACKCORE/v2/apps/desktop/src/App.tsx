import { useCallback, useEffect, useMemo, useState } from "react";
import { getOverview, setPaused } from "./lib/tauri";
import type { CapabilitySnapshot, RuntimeOverview } from "./lib/types";

const formatPct = (value?: number | null) => value == null ? "—" : `${Math.round(value)}%`;
const formatTemp = (value?: number | null) => value == null ? "—" : `${Math.round(value)}°`;

function CapabilityPill({ item }: { item: CapabilitySnapshot }) {
  return <div className={`capability capability-${item.state}`} title={item.evidence}>
    <span className="capability-dot" />
    <span>{item.label}</span>
    <b>{item.state.toUpperCase()}</b>
  </div>;
}

export default function App() {
  const [overview, setOverview] = useState<RuntimeOverview | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [command, setCommand] = useState("");
  const [lastAction, setLastAction] = useState("يراقب الجهاز بصمت");

  const refresh = useCallback(async () => {
    try {
      const data = await getOverview();
      setOverview(data);
      setError(null);
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    refresh();
    const timer = window.setInterval(refresh, 2500);
    return () => window.clearInterval(timer);
  }, [refresh]);

  const device = overview?.device;
  const runtime = overview?.runtime;
  const cDisk = device?.disks.find(d => d.mount.toUpperCase().startsWith("C:")) ?? device?.disks[0];
  const diskUsedPct = cDisk ? ((cDisk.totalGb - cDisk.freeGb) / cDisk.totalGb) * 100 : null;

  const readiness = useMemo(() => {
    if (!device) return 0;
    let score = 100;
    if (device.ramUsagePercent > 85) score -= 15;
    if (device.cpuUsagePercent > 90) score -= 10;
    if ((device.gpu?.temperatureC ?? 0) > 85) score -= 15;
    if (cDisk && cDisk.freeGb < 20) score -= 10;
    return Math.max(0, score);
  }, [device, cDisk]);

  const togglePause = async () => {
    if (!runtime) return;
    const next = !runtime.paused;
    await setPaused(next);
    setLastAction(next ? "تم إيقاف الأتمتة بأمان" : "تم استئناف Runtime");
    await refresh();
  };

  const runCommand = async () => {
    const value = command.trim();
    if (!value) return;
    if (/افحص|جهاز|حراره|حرارة|رام|gpu|cpu/i.test(value)) {
      setLastAction("يفحص حالة الجهاز الآن");
      await refresh();
    } else if (/وقف|إيقاف|توقف/i.test(value)) {
      await setPaused(true);
      setLastAction("تم تفعيل الإيقاف الآمن");
      await refresh();
    } else {
      setLastAction("تم استلام الهدف — محرك النوايا الكامل يدخل في milestone التالي");
    }
    setCommand("");
  };

  return <main className="shell">
    <div className="ambient ambient-a" />
    <div className="ambient ambient-b" />

    <header className="topbar">
      <div className="brand">
        <div className="brand-orb" />
        <div><b>D7 BLACKCORE</b><small>PERSONAL AUTONOMOUS OS · 2.0 dev.4</small></div>
      </div>
      <div className="top-actions">
        <span className="status-chip native"><i /> NATIVE RUNTIME</span>
        <span className="status-chip elite">ELITE</span>
        <button className={`stop ${runtime?.paused ? "paused" : ""}`} onClick={togglePause} disabled={!runtime}>
          {runtime?.paused ? "استئناف" : "إيقاف"}
        </button>
      </div>
    </header>

    <section className="hero glass">
      <div className="hero-copy">
        <span className="eyebrow">العقل تحت الغطاء، والواجهة بسيطة</span>
        <h1>وش تبي أسوي؟</h1>
        <p>{device ? `${device.hostname} · ${device.os} · آخر قراءة ${new Date(device.capturedAt).toLocaleTimeString("ar-SA")}` : "جارٍ ربط Runtime الحقيقي..."}</p>
      </div>
      <div className="command-row">
        <input value={command} onChange={e => setCommand(e.target.value)} onKeyDown={e => e.key === "Enter" && runCommand()} placeholder="مثال: افحص جهازي، جهزني للرانك، ابنِ لي برنامج..." />
        <button onClick={runCommand}>ابدأ</button>
      </div>
      <div className="quick-actions">
        <button onClick={() => { setCommand("افحص جهازي كامل"); }}>⚡ افحص الجهاز</button>
        <button onClick={() => setCommand("أبي ألعب رانك كود")}>🎮 لعب رانك</button>
        <button onClick={() => setCommand("ابنِ لي برنامج")}>🛠 ابنِ برنامج</button>
        <button onClick={() => setCommand("أبي أطلع دخل")}>💰 اصنع دخل</button>
        <button className="autopilot">● Autopilot</button>
      </div>
      <div className="metrics-strip">
        <div><span>CPU</span><b>{formatPct(device?.cpuUsagePercent)}</b></div>
        <div><span>RAM</span><b>{formatPct(device?.ramUsagePercent)}</b></div>
        <div><span>GPU</span><b>{formatPct(device?.gpu?.utilizationPercent)}</b></div>
        <div><span>TEMP</span><b>{formatTemp(device?.gpu?.temperatureC)}</b></div>
        <div><span>C:</span><b>{diskUsedPct == null ? "—" : `${Math.round(diskUsedPct)}% مستخدم`}</b></div>
      </div>
    </section>

    {error && <div className="error-banner">Runtime error: {error}</div>}

    <section className="cards-grid">
      <article className="glass card active-card">
        <span className="card-label">وش يسوي الآن؟</span>
        <h2>{runtime?.paused ? "متوقف بأمان" : lastAction}</h2>
        <p>{runtime?.paused ? "لا توجد أتمتة تنفذ حتى الاستئناف." : "Runtime حي ويراقب الحالة بدون تغيير إعداداتك."}</p>
        <div className="pulse-line"><span /></div>
      </article>

      <article className="glass card">
        <span className="card-label">الجهاز</span>
        <h2>{loading ? "يفحص..." : `${readiness}% جاهز`}</h2>
        <p>{device ? `${device.cpuName} · RAM ${device.ramTotalGb.toFixed(1)} GB · ${device.gpu?.name ?? "GPU غير مقروء"}` : "بانتظار Runtime"}</p>
        <span className="mini-tag">LIVE DEVICE TWIN</span>
      </article>

      <article className="glass card">
        <span className="card-label">الموافقات</span>
        <h2>0</h2>
        <p>لا يوجد إجراء حساس ينتظر قرارك.</p>
        <span className="mini-tag">APPROVAL FIREWALL</span>
      </article>

      <article className="glass card">
        <span className="card-label">الدخل</span>
        <h2>$0 مؤكد</h2>
        <p>Revenue لا يُحتسب إلا بإثبات دفع.</p>
        <span className="mini-tag">PAYMENT PROOF</span>
      </article>
    </section>

    <section className="lower-grid">
      <article className="glass capabilities-panel">
        <div className="section-title"><div><span>CAPABILITY TRUTH</span><h3>وش يقدر BLACKCORE يسوي الآن فعليًا؟</h3></div><button onClick={refresh}>تحديث</button></div>
        <div className="capabilities-list">
          {overview?.capabilities.map(item => <CapabilityPill key={item.id} item={item} />)}
          {!overview && <span className="muted">جارٍ تحميل القدرات...</span>}
        </div>
      </article>

      <article className="glass runtime-panel">
        <div className="section-title"><div><span>LIVE SYSTEM</span><h3>Runtime + D7 القديم</h3></div></div>
        <div className="runtime-rows">
          <div><span>OpenCode</span><b className={device?.openCode.available ? "ok" : "muted"}>{device?.openCode.available ? `ONLINE ${device.openCode.version ?? ""}` : "OFFLINE"}</b></div>
          <div><span>D7 Governor</span><b className={device?.governor.available ? "ok" : "muted"}>{device?.governor.available ? device.governor.state ?? "CONNECTED" : "NOT FOUND"}</b></div>
          <div><span>Processes</span><b>{device?.processCount ?? "—"}</b></div>
          <div><span>نشط</span><b>{device?.runningApps.join(" · ") || "—"}</b></div>
        </div>
      </article>
    </section>

    <footer>BLACKCORE 2.0 dev.4 · Native milestone · NO CLAIM WITHOUT PROOF</footer>
  </main>;
}
