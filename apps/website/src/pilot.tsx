import { useEffect, useState, type FormEvent } from "react";
import { createRoot } from "react-dom/client";
import { Brand, Icon } from "./ui";
import "./pilot.css";

const api = import.meta.env.VITE_AUTOBOTS_API_URL as string | undefined;
const cognito = import.meta.env.VITE_COGNITO_HOSTED_UI_URL as string | undefined;
const clientId = import.meta.env.VITE_COGNITO_CLIENT_ID as string | undefined;
const redirect = "https://autobots.origin-studio.in/pilot/index.html";
const download = import.meta.env.VITE_WINDOWS_DOWNLOAD_URL as string | undefined;
type Profile = { subject: string; email: string; username: string | null; purpose: string | null; enabled: number; invited_at: string; onboarded_at: string | null; lifetime_limit_usd?: string; lifetime_used_usd?: string };
type Session = { role: "owner" | "pilot"; subject?: string; profile?: Profile; lifetime_limit_usd?: string; lifetime_used_usd?: string };

function base64url(bytes: Uint8Array) { return btoa(String.fromCharCode(...bytes)).replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/, ""); }
async function signIn() {
  if (!cognito || !clientId) throw Error("Pilot sign-in is not configured yet.");
  const verifier = base64url(crypto.getRandomValues(new Uint8Array(32)));
  const state = base64url(crypto.getRandomValues(new Uint8Array(32)));
  const digest = new Uint8Array(await crypto.subtle.digest("SHA-256", new TextEncoder().encode(verifier)));
  sessionStorage.setItem("autobots-pilot-oauth", JSON.stringify({ verifier, state }));
  const query = new URLSearchParams({ client_id: clientId, response_type: "code", scope: "openid email profile", redirect_uri: redirect,
    state, code_challenge: base64url(digest), code_challenge_method: "S256" });
  location.assign(`${cognito}/oauth2/authorize?${query}`);
}
async function exchangeCode(): Promise<string | null> {
  const url = new URL(location.href);
  const code = url.searchParams.get("code");
  if (!code) return null;
  const saved = sessionStorage.getItem("autobots-pilot-oauth");
  sessionStorage.removeItem("autobots-pilot-oauth");
  history.replaceState(null, "", redirect);
  if (!saved || !cognito || !clientId) throw Error("Your sign-in session expired. Try again.");
  const { verifier, state } = JSON.parse(saved) as { verifier: string; state: string };
  if (state !== url.searchParams.get("state")) throw Error("Sign-in state did not match. Try again.");
  const response = await fetch(`${cognito}/oauth2/token`, { method: "POST", headers: { "Content-Type": "application/x-www-form-urlencoded" },
    body: new URLSearchParams({ grant_type: "authorization_code", client_id: clientId, code, redirect_uri: redirect, code_verifier: verifier }) });
  if (!response.ok) throw Error("Sign-in could not be completed. Try again.");
  const tokens = await response.json() as { access_token?: string };
  if (!tokens.access_token) throw Error("Sign-in returned no access token.");
  return tokens.access_token;
}
async function request<T>(path: string, token: string, init?: RequestInit): Promise<T> {
  if (!api) throw Error("The Autobots API is not configured yet.");
  const response = await fetch(`${api}${path}`, { ...init, headers: { Authorization: `Bearer ${token}`, ...(init?.body ? { "Content-Type": "application/json" } : {}) } });
  if (!response.ok) {
    if (response.status === 401) throw Error("Your session expired. Sign in again.");
    if (response.status === 403) throw Error("This account has not been invited or is not allowed to open this page.");
    if (response.status === 409) throw Error("That email is already invited or belongs to the owner.");
    throw Error("The request could not be completed. Try again.");
  }
  return response.json() as Promise<T>;
}

function PilotPortal() {
  const [token, setToken] = useState<string | null>(null);
  const [session, setSession] = useState<Session | null>(null);
  const [users, setUsers] = useState<Profile[]>([]);
  const [message, setMessage] = useState("");
  const [busy, setBusy] = useState(false);
  const [email, setEmail] = useState("");
  const [username, setUsername] = useState("");
  const [purpose, setPurpose] = useState("");
  useEffect(() => { exchangeCode().then(async key => {
    if (!key) return;
    const me = await request<Session>("/v1/pilot/me", key);
    setToken(key); setSession(me);
    if (me.role === "owner") setUsers((await request<{ pilots: Profile[] }>("/v1/admin/pilots", key)).pilots);
  }).catch(error => setMessage(error.message)); }, []);
  async function inviteUser(event: FormEvent) {
    event.preventDefault(); if (!token) return;
    setBusy(true); setMessage("");
    try {
      await request("/v1/admin/pilots", token, { method: "POST", body: JSON.stringify({ email: email.trim() }) });
      setUsers((await request<{ pilots: Profile[] }>("/v1/admin/pilots", token)).pilots);
      setMessage(`Invitation sent to ${email.trim()}.`); setEmail("");
    } catch (error) { setMessage((error as Error).message); } finally { setBusy(false); }
  }
  async function completeProfile(event: FormEvent) {
    event.preventDefault(); if (!token) return;
    setBusy(true); setMessage("");
    try {
      await request("/v1/pilot/profile", token, { method: "POST", body: JSON.stringify({ username, purpose }) });
      setSession(await request<Session>("/v1/pilot/me", token));
    } catch (error) { setMessage((error as Error).message); } finally { setBusy(false); }
  }
  const used = Number(session?.lifetime_used_usd || 0);
  return <div className="pilot-page"><header className="pilot-header"><a href="/"><Brand /></a><nav><a href="/">The experience</a><a href="/developer/index.html">The maker</a>{session && <button onClick={() => { setToken(null); setSession(null); location.assign(`${cognito}/logout?${new URLSearchParams({ client_id: clientId || "", logout_uri: redirect })}`); }}>Sign out</button>}</nav></header>
    <main className="pilot-main"><div className="pilot-intro"><span className="pilot-kicker"><span /> INVITE-ONLY ACCESS</span><h1>Your desktop,<br /><em>with a little help.</em></h1><p>One place to join the pilot, see your usage, and get the Windows app.</p></div>
      {!session && <section className="pilot-panel pilot-login"><div className="panel-icon"><Icon name="spark" size={23} /></div><div><span className="pilot-label">YOUR PILOT ACCOUNT</span><h2>Welcome aboard.</h2><p>Sign in with the email invited to Autobots. Your first sign-in lets you choose a permanent password.</p><button className="pilot-primary" onClick={() => signIn().catch(error => setMessage(error.message))}>Sign in to Autobots <span>↗</span></button><small>Only invited email addresses can use the app.</small></div></section>}
      {session?.role === "pilot" && !session.profile?.onboarded_at && <section className="pilot-panel"><span className="pilot-label">ONE LAST THING</span><h2>Tell us a bit about you.</h2><p>This helps us understand what the pilot should be useful for.</p><form onSubmit={completeProfile} className="pilot-form"><label>What should we call you?<input minLength={2} maxLength={60} required value={username} onChange={e => setUsername(e.target.value)} placeholder="Your name" /></label><label>What would you use Autobots for?<textarea minLength={10} maxLength={500} required value={purpose} onChange={e => setPurpose(e.target.value)} placeholder="A few words about the work you'd like help with" /></label><button className="pilot-primary" disabled={busy}>Finish setup <span>→</span></button></form></section>}
      {session?.role === "pilot" && session.profile?.onboarded_at && <section className="pilot-panel pilot-welcome"><span className="pilot-label">PILOT ACCESS ACTIVE</span><h2>Hey, {session.profile.username}.</h2><p>Your account is ready for the Windows app. Your AI use has a $10 lifetime limit, shared by desktop actions and voice transcription.</p><div className="pilot-meter"><div><strong>${used.toFixed(2)}</strong><span> of $10.00 used</span></div><div className="pilot-track"><i style={{ width: `${Math.min(100, used * 10)}%` }} /></div></div>{download && <a className="pilot-primary" href={download}>Download Windows pilot <span>↓</span></a>}</section>}
      {session?.role === "owner" && <><section className="pilot-panel pilot-admin-head"><div><span className="pilot-label">OWNER CONTROL ROOM</span><h2>Small group. Big ideas.</h2><p>Invite a pilot by email. Cognito sends a temporary password; each person sets their own password and completes a short profile on first sign-in.</p></div><div className="pilot-stat"><strong>{users.length.toString().padStart(2, "0")}</strong><span>invited pilots</span></div></section><section className="pilot-admin-grid"><div className="pilot-panel"><span className="pilot-label">SEND AN INVITATION</span><h3>Bring someone in.</h3><form className="pilot-form" onSubmit={inviteUser}><label>Email address<input type="email" required maxLength={254} placeholder="friend@example.com" value={email} onChange={e => setEmail(e.target.value)} /></label><button className="pilot-primary" disabled={busy}>Send pilot invite <span>→</span></button></form><small>Each pilot gets $10 of lifetime AI usage. Your owner account has no per-user lifetime cap.</small></div><div className="pilot-panel"><span className="pilot-label">ACCESS & USAGE</span><h3>Your pilot list.</h3><div className="pilot-list">{users.length === 0 ? <p>No one invited yet.</p> : users.map(user => <article key={user.subject}><div><strong>{user.username || user.email}</strong><span>{user.email}</span><small>{user.onboarded_at ? user.purpose : "Waiting for first sign-in"}</small></div><div className="pilot-spend"><b>${Number(user.lifetime_used_usd || 0).toFixed(2)}</b><span>/ $10 lifetime</span></div></article>)}</div></div></section><section className="pilot-panel pilot-owner-download"><div><span className="pilot-label">WINDOWS PILOT</span><h3>One download for everyone.</h3><p>Invited users can download the Windows build and sign in with their pilot accounts.</p></div>{download && <a className="pilot-primary" href={download}>Download build <span>↓</span></a>}</section></>}
      {message && <p className="pilot-message" role="status">{message}</p>}
    </main><footer className="pilot-footer">Autobots by Origin Studios <span>·</span> Windows 11 pilot <a href="/">Back to the website ↑</a></footer></div>;
}
createRoot(document.getElementById("root")!).render(<PilotPortal />);
