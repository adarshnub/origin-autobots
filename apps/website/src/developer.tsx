import { StrictMode, useState } from "react";
import { createRoot } from "react-dom/client";
import { Scene, useMotion } from "./Scene";
import "./style.css";

const profile = "https://www.linkedin.com/in/adarsh-viswam-95161016b/";

function Arrow() { return <span className="arrow" aria-hidden="true">↗</span>; }
function Brand() { return <a className="brand" href="/index.html" aria-label="Autobots home"><span className="brand-mark">A<span>·</span></span><span className="brand-word">AUTOBOTS<small>BY ORIGIN STUDIOS</small></span></a>; }

function Developer() {
  const motion = useMotion();
  const [menuOpen, setMenuOpen] = useState(false);
  return <>
    <Scene motion={motion} /><div className="noise" aria-hidden="true" /><div className="progress-track" aria-hidden="true"><div /></div>
    <header className="site-header"><Brand /><nav className={menuOpen ? "main-nav open" : "main-nav"} aria-label="Main navigation"><a href="/index.html#experience">Experience</a><a href="/index.html#demo">Demo</a><a href="/index.html#how">How it works</a><a href="#story" onClick={() => setMenuOpen(false)}>About Adarsh</a></nav><a className="header-cta" href="/index.html#download">GET THE APP <Arrow /></a><button className="menu-toggle" type="button" aria-label={menuOpen ? "Close menu" : "Open menu"} aria-expanded={menuOpen} onClick={() => setMenuOpen(!menuOpen)}>{menuOpen ? "×" : "☰"}</button></header>
    <main id="top">
      <section className="panel developer-hero" aria-labelledby="dev-title"><div className="section-kicker"><span>THE DEVELOPER / 01</span><span>ADARSH VISWAM</span></div><div className="developer-intro"><p className="overline">THE PERSON BEHIND AUTOBOTS</p><h1 id="dev-title">Adarsh<br /><em>Viswam.</em></h1><p>Software developer and the creator behind Autobots by Origin Studios, building a more visible way to work with intelligent desktop software.</p><div className="button-row"><a className="button button-lime" href={profile} target="_blank" rel="noreferrer">CONNECT ON LINKEDIN <Arrow /></a><a className="text-link" href="#story">EXPLORE THE STORY <span className="arrow">→</span></a></div></div><span className="profile-stamp">KOTTAYAM / INDIA ↗</span><a className="scroll-cue" href="#story"><span className="scroll-icon">↓</span><span>SCROLL TO DISCOVER</span></a></section>
      <section className="panel developer-story" id="story" aria-labelledby="story-title"><div className="section-kicker"><span>01 / THE STORY</span><span>PROFILE NOTES</span></div><div className="story-copy"><p className="overline">ENGINEERING & CURIOSITY</p><h2 id="story-title">Building things<br />people can <em>feel.</em></h2><p className="section-description">Adarsh is based in Kottayam, Kerala. His public profile connects software development at TGH Tech with a foundation at Rajiv Gandhi Institute of Technology, Kottayam. Autobots brings that product and engineering interest into a desktop assistant you can see working.</p></div><div className="profile-facts"><article><span>01 / LOCATION</span><h3>Kottayam,<br />Kerala, India</h3></article><article><span>02 / WORK</span><h3>TGH Tech</h3></article><article><span>03 / EDUCATION</span><h3>Rajiv Gandhi Institute<br />of Technology, Kottayam</h3></article></div></section>
      <section className="panel developer-work" id="work" aria-labelledby="work-title"><div className="section-kicker"><span>02 / SELECTED WORK</span><span>FROM THE PUBLIC PROFILE</span></div><div className="section-copy right-copy"><p className="overline">CODE TO PRODUCT</p><h2 id="work-title">Crafting the<br /><em>interaction.</em></h2><p className="section-description">His LinkedIn profile lists a customizable, mobile responsive React rich text editor project, along with full stack development coursework through The Odin Project.</p><div className="work-list"><a href={profile} target="_blank" rel="noreferrer"><span>01</span><strong>react-richtext-editor</strong><Arrow /></a><div><span>02</span><strong>The Odin Project · Full Stack Development</strong><Arrow /></div><div><span>03</span><strong>Autobots by Origin Studios</strong><Arrow /></div></div><p className="fine-print">Profile details sourced from the linked public LinkedIn page. Autobots is this site's project.</p></div></section>
      <section className="panel developer-end" aria-labelledby="end-title"><p className="overline">BACK TO THE PRODUCT</p><h2 id="end-title">Meet what he's<br /><em>building next.</em></h2><div className="button-row"><a className="button button-lime" href="/index.html">EXPLORE AUTOBOTS <Arrow /></a><a className="text-link" href={profile} target="_blank" rel="noreferrer">VIEW LINKEDIN PROFILE <Arrow /></a></div></section>
    </main>
    <footer className="site-footer"><Brand /><p>BUILT BY ADARSH VISWAM.</p><div><a href="/index.html">AUTOBOTS HOME</a><a href={profile} target="_blank" rel="noreferrer">LINKEDIN ↗</a><a href="#top">BACK TO TOP ↑</a></div><small>© {new Date().getFullYear()} ORIGIN STUDIOS</small></footer>
  </>;
}

createRoot(document.getElementById("root")!).render(<StrictMode><Developer /></StrictMode>);
