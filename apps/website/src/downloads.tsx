import { useEffect, useRef, useState } from "react";
import { createRoot } from "react-dom/client";
import { DownloadBot } from "./DownloadBot";
import { Brand, Icon, LINKEDIN } from "./ui";
import "./downloads.css";
import "./downloads-enhancements.css";

type Release = {
  version: string;
  publishedAt: string;
  headline: string;
  changes: string[];
  sizeBytes: number;
  platform: "windows";
};
type Catalog = { schemaVersion: number; releases: Release[] };
const versionPattern = /^\d+\.\d+\.\d+$/;
const downloadPath = (version: string) => `/downloads/windows/${version}/Autobots-Windows-${version}.zip`;

function Downloads() {
  const [releases, setReleases] = useState<Release[]>([]);
  const [status, setStatus] = useState("Loading published releases…");
  const [cheer, setCheer] = useState(false);
  const cheerTimeout = useRef<number | undefined>(undefined);
  useEffect(() => {
    fetch("/downloads/releases.json", { cache: "no-cache" }).then(async response => {
      if (!response.ok) throw Error("The release list is unavailable. Please try again shortly.");
      const catalog = await response.json() as Catalog;
      if (catalog.schemaVersion !== 1 || !Array.isArray(catalog.releases)) throw Error("The release list could not be read.");
      setReleases(catalog.releases.filter(release => release.platform === "windows" && versionPattern.test(release.version)));
      setStatus("");
    }).catch(error => setStatus((error as Error).message));
    return () => clearTimeout(cheerTimeout.current);
  }, []);
  function celebrate() {
    setCheer(true);
    clearTimeout(cheerTimeout.current);
    cheerTimeout.current = window.setTimeout(() => setCheer(false), 5000);
  }
  const latest = releases[0];
  return <div className="release-page">
    <header className="release-header"><a href="/"><Brand /></a><nav><a href="/">Explore Autobots</a><a href="/pilot/index.html">Pilot portal</a><a href="/developer/index.html">The maker</a></nav></header>
    <main>
      <section className="release-hero">
        <div className="release-hero-copy">
          <span className="release-kicker"><b /> THE WINDOWS PILOT</span>
          <h1>Ready when<br /><em>you are.</em></h1>
          <p>Pick a version, see what changed, and take Autobots for a spin. Your Windows desktop is about to get a little more capable.</p>
          <div className="release-hero-actions">
            {latest ? <a className="release-download release-hero-download" href={downloadPath(latest.version)} download onClick={celebrate}><Icon name="windows" size={19} /> Download latest · v{latest.version}<span>↓</span></a> : <a className="release-download release-hero-download" href="#versions">See Windows downloads <span>↓</span></a>}
            <a className="release-all-versions" href="#versions">See all versions <span>↗</span></a>
          </div>
          <p className="release-invite">Need an invite to sign in? <a href={LINKEDIN} target="_blank" rel="noreferrer">Ask Adarsh on LinkedIn ↗</a></p>
          <div className="release-hero-foot"><span><Icon name="windows" size={16} /> Windows 11 · x64</span><span>Invite-only access</span><span>Unsigned pilot builds</span></div>
        </div>
        <DownloadBot cheer={cheer} />
      </section>
      <section className="release-list-section" id="versions"><div className="release-list-head"><div><span className="release-kicker">CHOOSE YOUR VERSION</span><h2>Releases & notes.</h2></div><p>Each published version has its own download and a short list of changes.</p></div>
        {status && <div className="release-status" role="status">{status}</div>}
        {releases.map((release, index) => <article className="release-card" key={release.version}>
          <div className="release-version"><span>{index === 0 ? "LATEST RELEASE" : "PREVIOUS RELEASE"}</span><strong>v{release.version}</strong><small>{new Date(release.publishedAt).toLocaleDateString("en", { day: "numeric", month: "long", year: "numeric" })} · {(release.sizeBytes / 1048576).toFixed(1)} MiB</small></div>
          <div className="release-details"><h3>{release.headline}</h3><ul>{release.changes.map(change => <li key={change}>{change}</li>)}</ul></div>
          <div className="release-action"><a className="release-download" href={downloadPath(release.version)} download onClick={celebrate}><Icon name="windows" size={19} /> Download v{release.version} <span>↓</span></a></div>
        </article>)}
        {!status && releases.length === 0 && <div className="release-status">No public Windows pilot versions have been published yet.</div>}
      </section>
      <section className="release-steps"><span className="release-kicker">AFTER DOWNLOADING</span><h2>Three little steps.</h2><div><article><b>01</b><h3>Unzip it.</h3><p>Extract the Windows ZIP to a folder you control, then open Autobots.Desktop.exe.</p></article><article><b>02</b><h3>Sign in.</h3><p>Use your invited email. Choose a permanent password at first sign-in and complete your pilot profile.</p></article><article><b>03</b><h3>Give it a task.</h3><p>Type or speak a clear task, watch the visible steps, and use STOP whenever you need.</p></article></div><p className="release-note">The build is unsigned, so Windows may ask you to review it before opening. Your account is managed through the <a href="/pilot/index.html">pilot portal</a>. For an invitation, <a href={LINKEDIN} target="_blank" rel="noreferrer">message Adarsh on LinkedIn</a>.</p></section>
    </main><footer className="release-footer"><span>Autobots by Origin Studios</span><a href="/">Back to the experience ↑</a></footer>
  </div>;
}
createRoot(document.getElementById("root")!).render(<Downloads />);
