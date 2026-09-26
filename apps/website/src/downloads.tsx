import { useEffect, useRef, useState, type FormEvent } from "react";
import { createRoot } from "react-dom/client";
import { DownloadBot } from "./DownloadBot";
import { Brand, Icon, LINKEDIN } from "./ui";
import "./downloads.css";
import "./downloads-enhancements.css";

type Release = {
  version: string;
  assetType?: "installer" | "zip";
  publishedAt: string;
  headline: string;
  changes: string[];
  sizeBytes: number;
  platform: "windows";
};
type Catalog = { schemaVersion: number; releases: Release[] };
const versionPattern = /^\d+\.\d+\.\d+$/;
const downloadPath = (release: Release) => release.assetType === "installer"
  ? `/downloads/windows/${release.version}/Autobots-Setup-${release.version}.exe`
  : `/downloads/windows/${release.version}/Autobots-Windows-${release.version}.zip`;
const api = import.meta.env.VITE_AUTOBOTS_API_URL as string | undefined;

type Application = { email: string; name: string; profession: string; industry: string; purpose: string; website: string };
const emptyApplication: Application = { email: "", name: "", profession: "", industry: "", purpose: "", website: "" };

function Downloads() {
  const [releases, setReleases] = useState<Release[]>([]);
  const [status, setStatus] = useState("Loading published releases…");
  const [cheer, setCheer] = useState(false);
  const [applyOpen, setApplyOpen] = useState(false);
  const [application, setApplication] = useState<Application>(emptyApplication);
  const [applying, setApplying] = useState(false);
  const [applicationState, setApplicationState] = useState<"editing" | "received">("editing");
  const [applicationError, setApplicationError] = useState("");
  const cheerTimeout = useRef<number | undefined>(undefined);
  const applicationDialog = useRef<HTMLDialogElement>(null);
  useEffect(() => {
    fetch("/downloads/releases.json", { cache: "no-cache" }).then(async response => {
      if (!response.ok) throw Error("The release list is unavailable. Please try again shortly.");
      const catalog = await response.json() as Catalog;
      if (catalog.schemaVersion !== 1 || !Array.isArray(catalog.releases)) throw Error("The release list could not be read.");
      setReleases(catalog.releases.filter(release => release.platform === "windows" && versionPattern.test(release.version)));
      setStatus("");
    }).catch(error => setStatus((error as Error).message));
    if (location.hash === "#apply") setApplyOpen(true);
    return () => clearTimeout(cheerTimeout.current);
  }, []);
  useEffect(() => {
    const dialog = applicationDialog.current;
    if (!dialog) return;
    if (applyOpen && !dialog.open) dialog.showModal();
    if (!applyOpen && dialog.open) dialog.close();
  }, [applyOpen]);
  async function apply(event: FormEvent) {
    event.preventDefault();
    if (applying) return;
    setApplying(true);
    setApplicationError("");
    try {
      if (!api) throw Error("Applications are temporarily unavailable. Please try again later.");
      const response = await fetch(api + "/v1/pilot/applications", {
        method: "POST", headers: { "Content-Type": "application/json" },
        body: JSON.stringify(application)
      });
      if (!response.ok) {
        if (response.status === 429) throw Error("There have been too many requests. Please try again later.");
        throw Error("Your application could not be submitted. Please try again.");
      }
      setApplicationState("received");
      setApplication(emptyApplication);
    } catch (error) {
      setApplicationError((error as Error).message);
    } finally {
      setApplying(false);
    }
  }
  function celebrate() {
    setCheer(true);
    clearTimeout(cheerTimeout.current);
    cheerTimeout.current = window.setTimeout(() => setCheer(false), 5000);
  }
  const latest = releases[0];
  return <div className="release-page">
    <header className="release-header"><Brand /><nav><a href="/">Explore Autobots</a><a href="/pilot/index.html">Pilot portal</a><a href="/developer/index.html">The maker</a></nav></header>
    <main>
      <section className="release-hero">
        <div className="release-hero-copy">
          <span className="release-kicker"><b /> THE WINDOWS PILOT</span>
          <h1>Ready when<br /><em>you are.</em></h1>
          <p>Pick a version, see what changed, and take Autobots for a spin. Your Windows desktop is about to get a little more capable.</p>
          <div className="release-hero-actions">
            {latest ? <a className="release-download release-hero-download" href={downloadPath(latest)} download onClick={celebrate}><Icon name="windows" size={19} /> {latest.assetType === "installer" ? "Download Windows installer" : "Download latest ZIP"} · v{latest.version}<span>↓</span></a> : <a className="release-download release-hero-download" href="#versions">See Windows downloads <span>↓</span></a>}
            <button className="release-apply-button" type="button" onClick={() => { setApplicationState("editing"); setApplicationError(""); setApplyOpen(true); }}>Apply for pilot access <span aria-hidden="true">↗</span></button>
            <a className="release-all-versions" href="#versions">See all versions <span>↗</span></a>
          </div>
          <p className="release-invite">Access is approved by Adarsh. You can also <a href={LINKEDIN} target="_blank" rel="noreferrer">reach him on LinkedIn ↗</a>.</p>
          <div className="release-hero-foot"><span><Icon name="windows" size={16} /> Windows 11 · x64</span><span>Invite-only access</span><span>One-click setup</span></div>
        </div>
        <DownloadBot cheer={cheer} />
      </section>
      <section className="release-list-section" id="versions"><div className="release-list-head"><div><span className="release-kicker">CHOOSE YOUR VERSION</span><h2>Releases & notes.</h2></div><p>Each published version has its own download and a short list of changes.</p></div>
        {status && <div className="release-status" role="status">{status}</div>}
        {releases.map((release, index) => <article className="release-card" key={release.version}>
          <div className="release-version"><span>{index === 0 ? "LATEST RELEASE" : "PREVIOUS RELEASE"}</span><strong>v{release.version}</strong><small>{new Date(release.publishedAt).toLocaleDateString("en", { day: "numeric", month: "long", year: "numeric" })} · {(release.sizeBytes / 1048576).toFixed(1)} MiB</small></div>
          <div className="release-details"><h3>{release.headline}</h3><ul>{release.changes.map(change => <li key={change}>{change}</li>)}</ul></div>
          <div className="release-action"><a className="release-download" href={downloadPath(release)} download onClick={celebrate}><Icon name="windows" size={19} /> {release.assetType === "installer" ? "Download installer" : "Download ZIP"} · v{release.version} <span>↓</span></a></div>
        </article>)}
        {!status && releases.length === 0 && <div className="release-status">No public Windows pilot versions have been published yet.</div>}
      </section>
      <section className="release-steps"><span className="release-kicker">AFTER DOWNLOADING</span><h2>Three little steps.</h2><div><article><b>01</b><h3>Open setup.</h3><p>Open the downloaded installer once. It installs Autobots for your Windows account, adds it to Start, and opens the app. Older ZIP releases require extraction.</p></article><article><b>02</b><h3>Sign in.</h3><p>Use your invited email. Choose a permanent password at first sign-in and complete your pilot profile.</p></article><article><b>03</b><h3>Give it a task.</h3><p>Type or speak a clear task, watch the visible steps, and use STOP whenever you need.</p></article></div><p className="release-note">The pilot installer is unsigned, so Windows may ask you to review it before opening. Your account is managed through the <a href="/pilot/index.html">pilot portal</a>. For an invitation, <a href={LINKEDIN} target="_blank" rel="noreferrer">message Adarsh on LinkedIn</a>.</p></section>
    </main><footer className="release-footer"><span>Autobots by Origin Studios</span><a href="/">Back to the experience ↑</a></footer>
    <dialog className="pilot-apply-dialog" ref={applicationDialog} onClose={() => setApplyOpen(false)} onClick={event => { if (event.target === applicationDialog.current) setApplyOpen(false); }} aria-labelledby="pilot-apply-title">
      <div className="pilot-apply-content">
        <button className="pilot-apply-close" type="button" aria-label="Close application" onClick={() => setApplyOpen(false)}>×</button>
        {applicationState === "received" ? <div className="pilot-apply-success"><span className="pilot-apply-success-icon" aria-hidden="true">✓</span><span className="release-kicker">APPLICATION RECEIVED</span><h2 id="pilot-apply-title">You're on the list.</h2><p>If Adarsh approves your request, Cognito will email you an invitation with the next steps. Applying does not create an account.</p><button type="button" className="release-apply-submit" onClick={() => setApplyOpen(false)}>Back to downloads <span aria-hidden="true">→</span></button></div> :
          <><span className="release-kicker">APPLY FOR THE PILOT</span><h2 id="pilot-apply-title">Tell us a little about you.</h2><p>Autobots is open to a small group while we learn. Your application goes directly to the owner for review.</p>
            <form onSubmit={apply} className="pilot-apply-form">
              <label>Email address<input autoFocus type="email" autoComplete="email" required maxLength={254} placeholder="you@example.com" value={application.email} onChange={event => setApplication({ ...application, email: event.target.value })} /></label>
              <label>Full name<input type="text" autoComplete="name" required minLength={2} maxLength={80} placeholder="Your name" value={application.name} onChange={event => setApplication({ ...application, name: event.target.value })} /></label>
              <div className="pilot-apply-two"><label>Profession<input type="text" required minLength={2} maxLength={80} placeholder="AI engineer" value={application.profession} onChange={event => setApplication({ ...application, profession: event.target.value })} /></label><label>Industry<input type="text" required minLength={2} maxLength={80} placeholder="Technology" value={application.industry} onChange={event => setApplication({ ...application, industry: event.target.value })} /></label></div>
              <label>What would you like to try?<textarea required minLength={10} maxLength={500} placeholder="A few lines about the work you would give Autobots" value={application.purpose} onChange={event => setApplication({ ...application, purpose: event.target.value })} /></label>
              <label className="pilot-apply-honeypot" aria-hidden="true">Website<input type="text" tabIndex={-1} autoComplete="off" value={application.website} onChange={event => setApplication({ ...application, website: event.target.value })} /></label>
              {applicationError && <p className="pilot-apply-error" role="alert">{applicationError}</p>}
              <button type="submit" className="release-apply-submit" disabled={applying}>{applying ? "Sending application…" : "Send application"} <span aria-hidden="true">→</span></button>
              <small>Your details are used for this pilot access review. Only approved applicants receive a sign-in invitation.</small>
            </form></>}
      </div>
    </dialog>
  </div>;
}
createRoot(document.getElementById("root")!).render(<Downloads />);
