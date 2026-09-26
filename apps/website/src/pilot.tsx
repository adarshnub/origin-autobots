import { useEffect, useState, type FormEvent } from "react";
import { createRoot } from "react-dom/client";
import { Brand, Icon, LINKEDIN } from "./ui";
import companion from "./assets/bot-companion.png";
import "./pilot.css";

const api = import.meta.env.VITE_AUTOBOTS_API_URL as string | undefined;
const cognito = import.meta.env.VITE_COGNITO_HOSTED_UI_URL as string | undefined;
const clientId = import.meta.env.VITE_COGNITO_CLIENT_ID as string | undefined;
const redirect = "https://autobots.origin-studio.in/pilot/index.html";
const download = "/downloads/index.html";
const pilotLimit = 10;

type Profile = {
  subject: string;
  email: string;
  username: string | null;
  purpose: string | null;
  enabled: number;
  invited_at: string;
  onboarded_at: string | null;
  lifetime_limit_usd?: string;
  lifetime_used_usd?: string;
  revoked_at?: string | null;
  revocation_pending?: number;
};
type PilotApplication = {
  id: string;
  email: string;
  name: string;
  profession: string;
  industry: string;
  purpose: string;
  status: "pending" | "approving" | "approved" | "rejected";
  applied_at: string;
  reviewed_at: string | null;
};
type Session = {
  role: "owner" | "pilot";
  subject?: string;
  profile?: Profile;
  lifetime_limit_usd?: string;
  lifetime_used_usd?: string;
};
type Notice = { tone: "success" | "error" | "warning"; text: string } | null;
class AccessRevokedError extends Error {}

function base64url(bytes: Uint8Array) {
  return btoa(String.fromCharCode(...bytes)).replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/, "");
}

async function signIn() {
  if (!cognito || !clientId) throw Error("Pilot sign-in is not configured yet.");
  const verifier = base64url(crypto.getRandomValues(new Uint8Array(32)));
  const state = base64url(crypto.getRandomValues(new Uint8Array(32)));
  const digest = new Uint8Array(await crypto.subtle.digest("SHA-256", new TextEncoder().encode(verifier)));
  sessionStorage.setItem("autobots-pilot-oauth", JSON.stringify({ verifier, state }));
  const query = new URLSearchParams({
    client_id: clientId, response_type: "code", scope: "openid email profile", redirect_uri: redirect,
    state, code_challenge: base64url(digest), code_challenge_method: "S256"
  });
  location.assign(cognito + "/oauth2/authorize?" + query.toString());
}

async function exchangeCode(): Promise<string | null> {
  const url = new URL(location.href);
  if (url.searchParams.has("error")) {
    history.replaceState(null, "", redirect);
    throw Error("Sign-in was cancelled or could not be completed. Try again.");
  }
  const code = url.searchParams.get("code");
  if (!code) return null;
  const saved = sessionStorage.getItem("autobots-pilot-oauth");
  sessionStorage.removeItem("autobots-pilot-oauth");
  history.replaceState(null, "", redirect);
  if (!saved || !cognito || !clientId) throw Error("Your sign-in session expired. Try again.");
  const { verifier, state } = JSON.parse(saved) as { verifier: string; state: string };
  if (state !== url.searchParams.get("state")) throw Error("Sign-in state did not match. Try again.");
  const response = await fetch(cognito + "/oauth2/token", {
    method: "POST", headers: { "Content-Type": "application/x-www-form-urlencoded" },
    body: new URLSearchParams({ grant_type: "authorization_code", client_id: clientId, code, redirect_uri: redirect, code_verifier: verifier })
  });
  if (!response.ok) throw Error("Sign-in could not be completed. Try again.");
  const tokens = await response.json() as { access_token?: string };
  if (!tokens.access_token) throw Error("Sign-in returned no access token.");
  return tokens.access_token;
}

async function request<T>(path: string, token: string, init?: RequestInit): Promise<T> {
  if (!api) throw Error("The Autobots API is not configured yet.");
  const response = await fetch(api + path, {
    ...init,
    headers: { Authorization: "Bearer " + token, ...(init?.body ? { "Content-Type": "application/json" } : {}) }
  });
  if (!response.ok) {
    const body = await response.json().catch(() => null) as { detail?: { code?: string } } | null;
    const code = body?.detail?.code;
    if (code === "pilot_revoked") throw new AccessRevokedError("Your pilot access has been revoked. Autobots is signed out.");
    if (response.status === 401) throw Error("Your session expired. Sign in again.");
    if (response.status === 403) throw Error("This account has not been invited or is not allowed to open this page.");
    if (code === "application_not_pending") throw Error("This application was already reviewed. Refresh the queue.");
    if (code === "application_approval_uncertain") throw Error("The invitation result is uncertain. Review this application before taking another action.");
    if (code === "pilot_access_revoked") throw Error("This account was revoked and cannot be approved as a new pilot.");
    if (response.status === 409) throw Error("That email is already invited or belongs to the owner.");
    throw Error("The request could not be completed. Try again.");
  }
  return response.json() as Promise<T>;
}

function Feedback({ notice }: { notice: Notice }) {
  if (!notice) return null;
  return <p className={"pilot-feedback pilot-feedback-" + notice.tone} role={notice.tone === "error" ? "alert" : "status"}>
    <span aria-hidden="true">{notice.tone === "error" ? "!" : notice.tone === "warning" ? "!" : "✓"}</span>{notice.text}
  </p>;
}

function AccessSteps() {
  return <div className="pilot-access-steps" aria-label="How pilot access works">
    <div><b>01</b><span>Apply and get approved</span></div>
    <div><b>02</b><span>Choose your password</span></div>
    <div><b>03</b><span>Download the Windows app</span></div>
  </div>;
}

function PilotPortal() {
  const [token, setToken] = useState<string | null>(null);
  const [session, setSession] = useState<Session | null>(null);
  const [users, setUsers] = useState<Profile[]>([]);
  const [applications, setApplications] = useState<PilotApplication[]>([]);
  const [notice, setNotice] = useState<Notice>(null);
  const [reviewNotice, setReviewNotice] = useState<Notice>(null);
  const [authLoading, setAuthLoading] = useState(new URL(location.href).searchParams.has("code"));
  const [busy, setBusy] = useState(false);
  const [email, setEmail] = useState("");
  const [username, setUsername] = useState("");
  const [purpose, setPurpose] = useState("");
  const [search, setSearch] = useState("");
  const [reviewBusy, setReviewBusy] = useState<string | null>(null);
  const [confirmAction, setConfirmAction] = useState<{ kind: "reject" | "revoke"; id: string } | null>(null);

  async function loadOwnerData(key: string) {
    const [pilotList, applicationList] = await Promise.all([
      request<{ pilots: Profile[] }>("/v1/admin/pilots", key),
      request<{ applications: PilotApplication[] }>("/v1/admin/pilot-applications", key)
    ]);
    setUsers(pilotList.pilots);
    setApplications(applicationList.applications);
  }

  useEffect(() => {
    exchangeCode().then(async key => {
      if (!key) return;
      const me = await request<Session>("/v1/pilot/me", key);
      setToken(key);
      setSession(me);
      if (me.role === "owner") {
        try {
          await loadOwnerData(key);
        } catch {
          setReviewNotice({ tone: "error", text: "Your account opened, but the pilot data could not load. Use Refresh usage to try again." });
        }
      }
    }).catch(error => setNotice({ tone: "error", text: (error as Error).message }))
      .finally(() => setAuthLoading(false));
  }, []);

  useEffect(() => {
    if (!token || session?.role !== "pilot") return;
    let checking = false;
    const check = async () => {
      if (checking || document.hidden) return;
      checking = true;
      try {
        const current = await request<Session>("/v1/pilot/me", token);
        setSession(current);
      } catch (error) {
        if (error instanceof AccessRevokedError || (error as Error).message.includes("session expired")) {
          setToken(null);
          setSession(null);
          setNotice({ tone: "error", text: (error as Error).message });
        }
      } finally {
        checking = false;
      }
    };
    const timer = window.setInterval(check, 3000);
    const onVisible = () => { if (!document.hidden) void check(); };
    document.addEventListener("visibilitychange", onVisible);
    return () => { window.clearInterval(timer); document.removeEventListener("visibilitychange", onVisible); };
  }, [token, session?.role]);

  async function inviteUser(event: FormEvent) {
    event.preventDefault();
    if (!token || busy) return;
    const recipient = email.trim();
    setBusy(true);
    setNotice(null);
    try {
      await request("/v1/admin/pilots", token, { method: "POST", body: JSON.stringify({ email: recipient }) });
      setEmail("");
      setNotice({ tone: "success", text: "Invitation sent to " + recipient + "." });
      try {
        setUsers((await request<{ pilots: Profile[] }>("/v1/admin/pilots", token)).pilots);
      } catch {
        setNotice({ tone: "success", text: "Invitation sent to " + recipient + ". The pilot list could not refresh yet; please do not resend the invite." });
      }
    } catch (error) {
      setNotice({ tone: "error", text: (error as Error).message });
    } finally {
      setBusy(false);
    }
  }

  async function completeProfile(event: FormEvent) {
    event.preventDefault();
    if (!token || busy) return;
    setBusy(true);
    setNotice(null);
    try {
      await request("/v1/pilot/profile", token, { method: "POST", body: JSON.stringify({ username: username.trim(), purpose: purpose.trim() }) });
      setSession(await request<Session>("/v1/pilot/me", token));
    } catch (error) {
      setNotice({ tone: "error", text: (error as Error).message });
    } finally {
      setBusy(false);
    }
  }

  async function refreshUsage() {
    if (!token || busy || !session) return;
    setBusy(true);
    setNotice(null);
    try {
      if (session.role === "owner") {
        await loadOwnerData(token);
      } else {
        setSession(await request<Session>("/v1/pilot/me", token));
      }
    } catch (error) {
      setNotice({ tone: "error", text: (error as Error).message });
    } finally {
      setBusy(false);
    }
  }

  async function reviewApplication(application: PilotApplication, decision: "approve" | "reject") {
    if (!token || reviewBusy) return;
    setReviewBusy(application.id);
    setReviewNotice(null);
    setConfirmAction(null);
    try {
      await request("/v1/admin/pilot-applications/" + application.id + "/" + decision, token, { method: "POST" });
      setReviewNotice({ tone: "success", text: decision === "approve"
        ? "Approved " + application.name + ". An invitation was sent if this is a new account."
        : "Rejected " + application.name + "'s application." });
    } catch (error) {
      setReviewNotice({ tone: "error", text: (error as Error).message });
    } finally {
      try { await loadOwnerData(token); } catch { /* Preserve the action result; the owner can refresh the list. */ }
      setReviewBusy(null);
    }
  }

  async function revokeUser(user: Profile) {
    if (!token || reviewBusy) return;
    setReviewBusy(user.subject);
    setReviewNotice(null);
    setConfirmAction(null);
    try {
      const result = await request<{ status: string; provider_signout: string }>(
        "/v1/admin/pilots/" + user.subject + "/revoke", token, { method: "POST" });
      setReviewNotice(result.provider_signout === "completed"
        ? { tone: "success", text: "Access revoked for " + user.email + ". Active tasks were stopped and Cognito sessions were invalidated." }
        : { tone: "warning", text: "Autobots access is blocked for " + user.email + ". Cognito sign-out is pending; use Retry account disable in the roster." });
    } catch (error) {
      setReviewNotice({ tone: "error", text: (error as Error).message });
    } finally {
      try { await loadOwnerData(token); } catch { /* The owner can refresh the list. */ }
      setReviewBusy(null);
    }
  }

  function signOut() {
    setToken(null);
    setSession(null);
    if (cognito && clientId) {
      location.assign(cognito + "/logout?" + new URLSearchParams({ client_id: clientId, logout_uri: redirect }).toString());
    }
  }

  const activeUsers = users.filter(user => Boolean(user.enabled && user.onboarded_at));
  const filteredUsers = users.filter(user => (user.email + " " + (user.username || "")).toLowerCase().includes(search.toLowerCase()));
  const totalUsed = users.reduce((sum, user) => sum + (Number(user.lifetime_used_usd) || 0), 0);
  const pendingApplications = applications.filter(application => application.status === "pending").length;
  const orderedApplications = [...applications].sort((first, second) =>
    Number(second.status === "pending") - Number(first.status === "pending") ||
    second.applied_at.localeCompare(first.applied_at));
  const used = Number(session?.lifetime_used_usd) || 0;
  const remaining = Math.max(0, pilotLimit - used);

  return <div className="pilot-page">
    <header className="pilot-header">
      <Brand />
      <nav aria-label="Pilot navigation">
        <a href="/downloads/index.html">Windows downloads</a>
        <a href="/">Explore Autobots</a>
        {session && <button type="button" onClick={signOut}>Sign out <span aria-hidden="true">↗</span></button>}
      </nav>
    </header>
    <main className="pilot-main">
      {!session && <div className="pilot-entry-layout">
        <section className="pilot-entry-copy">
          <span className="pilot-eyebrow"><span className="pilot-signal" /> AUTOBOTS PILOT · WINDOWS 11</span>
          <h1>Your seat at<br /><em>the desktop.</em></h1>
          <p className="pilot-lead">Sign in to manage your pilot access. Your invitation is the key to using Autobots on Windows.</p>
          <div className="pilot-entry-actions">
            <button className="pilot-primary" type="button" disabled={authLoading} onClick={() => signIn().catch(error => setNotice({ tone: "error", text: (error as Error).message }))}>
              {authLoading ? "Completing sign-in…" : "Sign in to the pilot"} <span aria-hidden="true">↗</span>
            </button>
            <a className="pilot-secondary" href="/downloads/index.html#apply">Apply for access <span aria-hidden="true">↗</span></a>
          </div>
          <Feedback notice={notice} />
          <p className="pilot-access-note"><Icon name="shield" size={17} /><span>Only approved email addresses can sign in. You can <a href={download}>browse Windows releases</a> or <a href={LINKEDIN} target="_blank" rel="noreferrer">message Adarsh on LinkedIn</a>.</span></p>
          <AccessSteps />
        </section>
        <aside className="pilot-entry-visual" aria-label="Autobots pilot companion">
          <div className="pilot-visual-top"><span>YOUR DESKTOP CO-PILOT</span><span>01 / PILOT ACCESS</span></div>
          <div className="pilot-visual-halo" aria-hidden="true" />
          <img src={companion} alt="Illustration of the Autobots companion holding a task card" />
          <div className="pilot-visual-card"><span className="pilot-mini-signal" /> Ready when you are.<small>Download the app. Bring your own task.</small></div>
          <div className="pilot-visual-bottom"><span>SEE THE SCREEN</span><span>MAKE A MOVE</span><span>STAY IN CONTROL</span></div>
        </aside>
      </div>}

      {session?.role === "pilot" && !session.profile?.onboarded_at && <div className="pilot-profile-layout">
        <section className="pilot-panel pilot-profile-card">
          <span className="pilot-eyebrow">STEP 02 / 03 · YOUR PROFILE</span>
          <h1>Make it yours.</h1>
          <p className="pilot-lead">Two quick details help Adarsh understand how you plan to use the pilot.</p>
          <form onSubmit={completeProfile} className="pilot-form">
            <label>What should we call you?<input autoComplete="nickname" minLength={2} maxLength={60} required value={username} onChange={event => setUsername(event.target.value)} placeholder="Your name" /></label>
            <label>What would you use Autobots for?<textarea minLength={10} maxLength={500} required value={purpose} onChange={event => setPurpose(event.target.value)} placeholder="For example, organizing meetings across my apps" /><span className="pilot-field-help">The pilot owner can see this answer. {purpose.length}/500</span></label>
            <button className="pilot-primary" disabled={busy}>{busy ? "Saving your profile…" : "Finish setup"} <span aria-hidden="true">→</span></button>
          </form>
          <Feedback notice={notice} />
        </section>
        <aside className="pilot-side-note"><span className="pilot-eyebrow">UP NEXT</span><h2>Your Windows app is waiting.</h2><p>After setup, choose a version, sign in to the desktop app with this account, and start with one clear task.</p><a href={download}>Browse available versions <span aria-hidden="true">↗</span></a></aside>
      </div>}

      {session?.role === "pilot" && session.profile?.onboarded_at && <div className="pilot-account">
        <div className="pilot-account-heading"><div><span className="pilot-eyebrow"><span className="pilot-signal" /> YOUR ACCESS IS ACTIVE</span><h1>You're in, {session.profile.username}.</h1><p>Your pilot account is ready. Download the Windows app and sign in with the same email.</p></div><a className="pilot-primary" href={download}><Icon name="windows" size={19} /> Get the Windows app <span aria-hidden="true">↗</span></a></div>
        <div className="pilot-account-grid">
          <section className="pilot-panel pilot-budget-card"><div className="pilot-budget-top"><span className="pilot-eyebrow">AI USAGE · LIFETIME</span><button className="pilot-refresh-button" type="button" disabled={busy} onClick={refreshUsage}>{busy ? "Refreshing…" : "Refresh usage ↻"}</button></div><div className="pilot-budget-numbers"><strong>${used.toFixed(2)}</strong><span>of $10.00 used</span></div><div className="pilot-track" role="progressbar" aria-label="AI usage" aria-valuenow={Math.min(10, used)} aria-valuemin={0} aria-valuemax={10}><i style={{ width: Math.min(100, used * 10) + "%" }} /></div><p><strong>${remaining.toFixed(2)}</strong> remaining for desktop actions and voice transcription.</p></section>
          <section className="pilot-panel pilot-next-card"><span className="pilot-eyebrow">YOUR NEXT MOVE</span><h2>Give it one clear task.</h2><p>Open Autobots on Windows, describe what needs doing, then watch each visible step. The local STOP control is always there.</p><a href={download}>View versions and notes <span aria-hidden="true">→</span></a></section>
        </div>
        <Feedback notice={notice} />
      </div>}

      {session?.role === "owner" && <div className="pilot-owner">
        <div className="pilot-owner-heading"><div><span className="pilot-eyebrow"><span className="pilot-signal" /> OWNER CONTROL ROOM</span><h1>Your pilot crew.</h1><p>Review applications, manage access, and keep an eye on lifetime AI usage.</p></div><div className="pilot-heading-actions"><button className="pilot-refresh-button" type="button" disabled={busy} onClick={refreshUsage}>{busy ? "Refreshing…" : "Refresh data ↻"}</button><a className="pilot-outline-button" href={download}><Icon name="windows" size={18} /> Windows releases <span aria-hidden="true">↗</span></a></div></div>
        <div className="pilot-metrics" aria-label="Pilot summary">
          <div><span>INVITED</span><strong>{users.length.toString().padStart(2, "0")}</strong><small>Total pilot accounts</small></div>
          <div><span>READY TO USE</span><strong>{activeUsers.length.toString().padStart(2, "0")}</strong><small>Completed their profile</small></div>
          <div><span>AI USAGE</span><strong>${totalUsed.toFixed(2)}</strong><small>Across invited pilots</small></div>
        </div>
        <section className="pilot-panel pilot-applications-card">
          <div className="pilot-roster-heading"><div><span className="pilot-eyebrow">APPLICATIONS / OWNER REVIEW</span><h2>People at the door.</h2><p>Review the details before granting an account. Approval sends a Cognito invitation to new pilots.</p></div><span className="pilot-roster-count">{pendingApplications} pending</span></div>
          <Feedback notice={reviewNotice} />
          <div className="pilot-applications-list">{orderedApplications.length === 0 ? <div className="pilot-empty"><span aria-hidden="true">✦</span><strong>No applications yet.</strong><p>Requests from the downloads page will appear here.</p></div> : orderedApplications.map(application =>
            <article className="pilot-application" key={application.id}>
              <div className="pilot-application-head"><div><strong>{application.name}</strong><span>{application.email}</span></div><span className={"pilot-status pilot-status-" + application.status}>{application.status === "approving" ? "Needs review" : application.status}</span></div>
              <p className="pilot-application-meta">{application.profession} <span aria-hidden="true">·</span> {application.industry}</p>
              <p className="pilot-application-purpose">{application.purpose}</p>
              <div className="pilot-application-foot"><small>Applied {new Date(application.applied_at).toLocaleDateString("en", { day: "numeric", month: "short", year: "numeric" })}</small>
                {application.status === "approving" ? <span className="pilot-review-warning">Invitation outcome is uncertain. Check Cognito before another action.</span> :
                  (application.status === "pending" || application.status === "rejected") && <div className="pilot-review-actions">
                    <button type="button" className="pilot-review-approve" disabled={reviewBusy !== null} onClick={() => reviewApplication(application, "approve")}>{reviewBusy === application.id ? "Working…" : "Approve & invite"}</button>
                    {application.status === "pending" && (confirmAction?.kind === "reject" && confirmAction.id === application.id ? <>
                      <button type="button" className="pilot-review-danger" disabled={reviewBusy !== null} onClick={() => reviewApplication(application, "reject")}>Confirm reject</button>
                      <button type="button" className="pilot-review-cancel" onClick={() => setConfirmAction(null)}>Cancel</button>
                    </> : <button type="button" className="pilot-review-cancel" disabled={reviewBusy !== null} onClick={() => setConfirmAction({ kind: "reject", id: application.id })}>Reject</button>)}
                  </div>}
              </div>
            </article>)}</div>
        </section>
        <div className="pilot-admin-grid">
          <section className="pilot-panel pilot-invite-card">
            <span className="pilot-eyebrow">01 / INVITE SOMEONE</span><h2>Bring someone in.</h2><p>Cognito emails a temporary password. Each person chooses a new password and completes their profile on first sign-in.</p>
            <form className="pilot-form" onSubmit={inviteUser}><label>Email address<input type="email" autoComplete="email" required maxLength={254} placeholder="friend@example.com" value={email} onChange={event => setEmail(event.target.value)} /></label><button className="pilot-primary" disabled={busy || !email.trim()}>{busy ? "Sending invitation…" : "Send pilot invite"} <span aria-hidden="true">→</span></button></form>
            <Feedback notice={notice} />
            <p className="pilot-small-print">Each pilot has a $10 lifetime AI usage limit. Your owner account has no per-user lifetime cap.</p>
          </section>
          <section className="pilot-panel pilot-roster-card">
            <div className="pilot-roster-heading"><div><span className="pilot-eyebrow">02 / PILOT ROSTER</span><h2>Everyone aboard.</h2></div><span className="pilot-roster-count">{users.length} people</span></div>
            <label className="pilot-search"><Icon name="search" size={18} /><span className="sr-only">Search pilot users</span><input type="search" value={search} onChange={event => setSearch(event.target.value)} placeholder="Search by name or email" /></label>
            <div className="pilot-list">{users.length === 0 ? <div className="pilot-empty"><span aria-hidden="true">✦</span><strong>No one invited yet.</strong><p>Your first pilot will appear here after you send an invitation.</p></div> : filteredUsers.length === 0 ? <div className="pilot-empty"><strong>No matching pilots.</strong><p>Try another name or email address.</p></div> : filteredUsers.map(user => {
              const pilotUsed = Number(user.lifetime_used_usd) || 0;
              return <article key={user.subject} className={"pilot-person " + (user.enabled ? user.onboarded_at ? "is-active" : "is-pending" : "is-revoked")}>
                <div className="pilot-person-avatar" aria-hidden="true">{(user.username || user.email).slice(0, 1).toUpperCase()}</div>
                <div className="pilot-person-info"><strong>{user.username || user.email}</strong>{user.username && <span>{user.email}</span>}<small>{!user.enabled ? user.revocation_pending ? "Revoked · Cognito sign-out pending" : "Access revoked" : user.onboarded_at ? "Profile complete" : "Waiting for first sign-in"}</small>{user.purpose && <p>{user.purpose}</p>}</div>
                <div className="pilot-person-usage"><b>${pilotUsed.toFixed(2)}</b><span>of $10 used</span><i><i style={{ width: Math.min(100, pilotUsed * 10) + "%" }} /></i></div>
                <div className="pilot-person-actions">{user.enabled ? (confirmAction?.kind === "revoke" && confirmAction.id === user.subject ? <>
                  <button type="button" className="pilot-review-danger" disabled={reviewBusy !== null} onClick={() => revokeUser(user)}>Confirm revoke</button>
                  <button type="button" className="pilot-review-cancel" onClick={() => setConfirmAction(null)}>Cancel</button>
                </> : <button type="button" className="pilot-review-cancel" disabled={reviewBusy !== null} onClick={() => setConfirmAction({ kind: "revoke", id: user.subject })}>Revoke access</button>) :
                  Boolean(user.revocation_pending) && <button type="button" className="pilot-review-cancel" disabled={reviewBusy !== null} onClick={() => revokeUser(user)}>Retry account disable</button>}
                </div>
              </article>;
            })}</div>
          </section>
        </div>
      </div>}
    </main>
    <footer className="pilot-footer"><span>Autobots by Origin Studios</span><span>Windows 11 pilot · Invitation only</span><a href="/">Back to the website ↑</a></footer>
  </div>;
}

createRoot(document.getElementById("root")!).render(<PilotPortal />);
