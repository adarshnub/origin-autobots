import { useState } from "react";
import type { ReactNode } from "react";
import eyesLogo from "./assets/autobots-eyes.svg";
export const LINKEDIN = "https://www.linkedin.com/in/adarsh-viswam-95161016b/";

export function Icon({ name, size = 20 }: { name: string; size?: number }) {
  const paths: Record<string, ReactNode> = {
    arrow: <><path d="M5 12h14m-6-6 6 6-6 6" /></>,
    diagonal: <><path d="M6 18 18 6M6 6h12v12" /></>,
    cursor: <path d="m5 3 14 9-7 1-3 7Z" />,
    play: <path d="m9 5 11 7-11 7Z" />,
    pause: <><path d="M8 5v14M16 5v14" /></>,
    shield: <><path d="m12 3 8 3v6c0 5-8 9-8 9s-8-4-8-9V6Z" /><path d="m8 12 3 3 5-6" /></>,
    search: <><circle cx="10.8" cy="10.8" r="6.7" /><path d="m16 16 4.4 4.4" /></>,
    command: <><rect x="3" y="3" width="18" height="18" rx="5" /><path d="m8 8 4 4-4 4m6 0h3" /></>,
    window: <><rect x="3" y="4" width="18" height="16" rx="3" /><path d="M3 9h18M7 6.5h.01M10 6.5h.01" /></>,
    windows: <><path d="M3 4h8v8H3zm10 0h8v8h-8zM3 14h8v8H3zm10 0h8v8h-8z" fill="currentColor" stroke="none" /></>,
    spark: <><path d="m12 3 2.5 6.5L21 12l-6.5 2.5L12 21l-2.5-6.5L3 12l6.5-2.5Z" /></>,
    close: <path d="m6 6 12 12M6 18 18 6" />,
    menu: <path d="M4 7h16M4 12h16M4 17h16" />,
    expand: <><path d="M9 4H4v5m11-5h5v5M4 15v5h5m6 0h5v-5" /></>,
    check: <path d="m5 12 5 5L20 7" />,
    mic: <><rect x="9" y="3" width="6" height="11" rx="3" /><path d="M6 10v2a6 6 0 0 0 12 0v-2m-6 8v3m-3 0h6" /></>,
  };
  return <svg width={size} height={size} viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.7" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">{paths[name] || paths.spark}</svg>;
}

export function EyesLogo({ className = "" }: { className?: string }) {
  return <img className={"eyes-logo " + className} src={eyesLogo} alt="" aria-hidden="true" width="40" height="40" />;
}

export function Brand() {
  return <a className="brand" href="/" aria-label="Autobots home"><EyesLogo /><span>autobots<span className="brand-by">by Origin Studios</span></span></a>;
}

export function Header({ developer = false }: { developer?: boolean }) {
  const [open,setOpen] = useState(false);
  const prefix = developer ? "/" : "";
  return <header className="site-header"><Brand /><nav className={open ? "nav-links open" : "nav-links"} aria-label="Main navigation"><a href={`${prefix}#product`} onClick={() => setOpen(false)}>The experience</a><a href={`${prefix}#in-action`} onClick={() => setOpen(false)}>In action</a><a href={`${prefix}#how`} onClick={() => setOpen(false)}>How it works</a><a href="/developer/index.html">The maker <Icon name="diagonal" size={13} /></a><a href="/pilot/index.html">Pilot portal <Icon name="diagonal" size={13} /></a></nav><a className="nav-download" href="/downloads/index.html">Get Autobots <Icon name="arrow" size={16} /></a><button className="menu-toggle" type="button" aria-label={open ? "Close navigation" : "Open navigation"} aria-expanded={open} onClick={() => setOpen(!open)}><Icon name={open ? "close" : "menu"} /></button></header>;
}

export function Footer() {
  return <footer className="site-footer"><Brand /><p>See the work. Stay in control.</p><div><a href="/developer/index.html">Meet the maker</a><a href={LINKEDIN} target="_blank" rel="noreferrer">LinkedIn <Icon name="diagonal" size={14} /></a><a href="#top">Back to top ↑</a></div><small>© {new Date().getFullYear()} Origin Studios</small></footer>;
}
