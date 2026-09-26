import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { BotsScene, useFlight } from "./BotsScene";
import { Header, Footer, Icon, LINKEDIN } from "./ui";
import "./design.css";

function Developer() {
  const motion = useFlight();
  return <><BotsScene motion={motion} /><Header developer /><main id="top" className="developer-main">
    <section className="developer-hero section" data-flight-section><span className="eyebrow-pill"><span className="status-dot" /> The human behind the helpers</span><h1>Meet Adarsh.<br /><span>The maker of Autobots.</span></h1><p>Software developer. Curious builder.<br />Creating a more visible way to work with desktop intelligence.</p><a className="button primary" href={LINKEDIN} target="_blank" rel="noreferrer">Connect on LinkedIn <Icon name="diagonal" size={18} /></a><div className="profile-coordinates">Kottayam, Kerala, India <span>↗</span></div></section>
    <section className="section developer-story" data-flight-section><div className="section-heading"><span className="eyebrow">A LITTLE BACKGROUND</span><h2>From an idea<br /><span>to something you can use.</span></h2><p>Adarsh Viswam is the developer behind Autobots by Origin Studios. His public profile connects his work at TGH Tech with a foundation at Rajiv Gandhi Institute of Technology, Kottayam.</p></div><div className="profile-grid"><article><Icon name="window" size={27} /><span>Work</span><h3>TGH Tech</h3><p>Software development</p></article><article><Icon name="command" size={27} /><span>Education</span><h3>Rajiv Gandhi Institute<br />of Technology</h3><p>Kottayam · 2018–2022</p></article><article><Icon name="spark" size={27} /><span>Always learning</span><h3>The Odin Project</h3><p>Full stack development coursework</p></article></div></section>
    <section className="section developer-projects" data-flight-section><div className="section-heading"><span className="eyebrow">THINGS HE'S BUILDING</span><h2>Small details.<br /><span>Useful possibilities.</span></h2></div><div className="project-list"><a href="/"><div className="project-symbol"><Icon name="cursor" size={30} /></div><div><h3>Autobots</h3><p>A visible desktop assistant by Origin Studios.</p></div><Icon name="diagonal" /></a><a href={LINKEDIN} target="_blank" rel="noreferrer"><div className="project-symbol peach"><Icon name="command" size={30} /></div><div><h3>react-richtext-editor</h3><p>A customizable, responsive React rich text editor listed on his profile.</p></div><Icon name="diagonal" /></a></div><p className="under-note">Background and project details from <a href={LINKEDIN} target="_blank" rel="noreferrer">Adarsh's public LinkedIn profile ↗</a>.</p></section>
    <section className="section developer-connect" data-flight-section><span className="eyebrow">IDEAS START WITH A HELLO</span><h2>Let's make<br /><span>something possible.</span></h2><div className="hero-actions"><a className="button primary" href={LINKEDIN} target="_blank" rel="noreferrer">Say hello on LinkedIn <Icon name="diagonal" size={18} /></a><a className="button secondary" href="/">Explore Autobots <Icon name="arrow" size={18} /></a></div></section>
  </main><Footer /></>;
}

createRoot(document.getElementById("root")!).render(<StrictMode><Developer /></StrictMode>);
