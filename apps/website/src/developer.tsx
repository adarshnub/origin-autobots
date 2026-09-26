import { StrictMode, useState } from "react";
import { createRoot } from "react-dom/client";
import { BotsScene, useFlight } from "./BotsScene";
import { Header, Footer, Icon, LINKEDIN } from "./ui";
import { developerProjects, GITHUB, projectCategories } from "./developerProjects";
import type { DeveloperProject, ProjectCategory } from "./developerProjects";
import "./design.css";
import "./developer.css";

const categoryColors: Record<ProjectCategory, string> = {
  "All projects": "violet", "AI & automation": "violet", "Developer tools": "blue",
  "Web products": "peach", "3D & games": "mint",
};

function GitHubIcon() {
  return <svg width="19" height="19" viewBox="0 0 24 24" fill="currentColor" aria-hidden="true"><path d="M12 .9a11.1 11.1 0 0 0-3.51 21.63c.55.1.76-.24.76-.54v-2.07c-3.09.67-3.74-1.31-3.74-1.31-.5-1.28-1.23-1.62-1.23-1.62-1.01-.69.08-.68.08-.68 1.12.08 1.7 1.14 1.7 1.14.99 1.7 2.6 1.21 3.24.93.1-.72.39-1.21.7-1.49-2.47-.28-5.07-1.24-5.07-5.49 0-1.21.43-2.2 1.14-2.98-.11-.28-.5-1.41.11-2.94 0 0 .93-.3 3.05 1.14a10.62 10.62 0 0 1 5.55 0c2.12-1.44 3.05-1.14 3.05-1.14.61 1.53.22 2.66.11 2.94.71.78 1.14 1.77 1.14 2.98 0 4.27-2.6 5.21-5.08 5.49.4.35.75 1.02.75 2.06v3.04c0 .3.2.65.76.54A11.1 11.1 0 0 0 12 .9Z" /></svg>;
}

function ProjectCard({ project }: { project: DeveloperProject }) {
  const source = GITHUB + "/" + project.repo;
  return <article className={"portfolio-card tone-" + categoryColors[project.category]}>
    <div className="portfolio-card-top"><span className="portfolio-mark" aria-hidden="true">{project.mark}</span><span className="portfolio-kind">{project.kind}</span></div>
    <h3><a href={source} target="_blank" rel="noreferrer">{project.name}<Icon name="diagonal" size={19} /></a></h3>
    <p>{project.description}</p><p className="portfolio-detail">{project.detail}</p>
    <ul className="portfolio-stack" aria-label={project.name + " technologies"}>{project.stack.map(tech => <li key={tech}>{tech}</li>)}</ul>
    <a className="portfolio-source" href={source} target="_blank" rel="noreferrer" aria-label={"View " + project.name + " repository on GitHub"}><GitHubIcon /><span>View repository</span><Icon name="arrow" size={16} /></a>
  </article>;
}

function ProjectGallery() {
  const [category, setCategory] = useState<ProjectCategory>("All projects");
  const [query, setQuery] = useState("");
  const normalizedQuery = query.trim().toLowerCase();
  const projects = developerProjects.filter(project =>
    (category === "All projects" || project.category === category) &&
    [project.name, project.repo, project.kind, project.description, project.detail, ...project.stack].join(" ").toLowerCase().includes(normalizedQuery)
  );
  return <>
    <div className="portfolio-controls">
      <div className="portfolio-filters" role="group" aria-label="Filter projects by category">{projectCategories.map(item => <button key={item} type="button" aria-pressed={category === item} onClick={() => setCategory(item)}>{item}<span>{item === "All projects" ? developerProjects.length : developerProjects.filter(project => project.category === item).length}</span></button>)}</div>
      <label className="portfolio-search"><span>Find a project</span><input type="search" value={query} onChange={event => setQuery(event.target.value)} placeholder="Search name, idea, or technology…" /></label>
    </div>
    <div className="portfolio-results-meta"><p role="status" aria-live="polite" aria-atomic="true">{projects.length} {projects.length === 1 ? "project" : "projects"}{category !== "All projects" ? " in " + category : " to explore"}</p><span>From the public work of @adarshnub</span></div>
    <div className="portfolio-grid">{projects.map(project => <ProjectCard key={project.repo} project={project} />)}</div>
    {projects.length === 0 && <div className="portfolio-empty"><h3>No projects found.</h3><p>Try a different name or technology.</p><button className="button secondary" type="button" onClick={() => { setCategory("All projects"); setQuery(""); }}>Show all projects <Icon name="arrow" size={17} /></button></div>}
    <div className="portfolio-more"><p>There’s always another idea in the works.</p><a href={GITHUB + "?tab=repositories"} target="_blank" rel="noreferrer">Explore all repositories <Icon name="diagonal" size={17} /></a></div>
  </>;
}

function Developer() {
  const motion = useFlight();
  return <><BotsScene motion={motion} /><Header developer /><main id="top" className="developer-main">
    <section className="developer-hero section" data-flight-section>
      <span className="eyebrow-pill"><span className="status-dot" /> The human behind the helpers</span>
      <h1>Adarsh Viswam.<br /><span>Always building.</span></h1>
      <p>A self-taught software developer at <strong>Ant Venture.ai</strong><br className="desktop-break" /> and the maker of Autobots by Origin Studios.</p>
      <p className="developer-intro">From AI tools to playful 3D worlds, I build things that turn a little curiosity into something you can use.</p>
      <div className="hero-actions"><a className="button primary" href="#projects">Explore my work <Icon name="arrow" size={18} /></a><a className="button secondary" href={GITHUB} target="_blank" rel="noreferrer"><GitHubIcon /> @adarshnub</a></div>
      <div className="profile-coordinates">Kottayam, Kerala, India <span aria-hidden="true">↗</span> <a href={LINKEDIN} target="_blank" rel="noreferrer">Find me on LinkedIn</a></div>
      <div className="developer-index"><a href="#about">01 <span>A little about me</span></a><a href="#spotlight">02 <span>On the workbench</span></a><a href="#projects">03 <span>{developerProjects.length} selected projects</span></a></div>
    </section>
    <section id="about" className="section developer-story" data-flight-section>
      <div className="developer-about-layout">
        <div className="section-heading"><span className="eyebrow">A LITTLE BACKGROUND</span><h2>Curiosity starts it.<br /><span>Building makes it real.</span></h2><p>I’m Adarsh, a software developer based in Kottayam. My work spans web products, developer tools, desktop automation, and interactive experiences.</p><p>I enjoy connecting the interface you see with the systems underneath it: APIs, data, model integrations, and the details that make an idea usable.</p></div>
        <div className="developer-profile-note"><div className="developer-monogram" aria-hidden="true">av<span>↗</span></div><span className="eyebrow">CURRENT CHAPTER</span><h3>Ant Venture.ai</h3><p>Software development</p><a href={GITHUB} target="_blank" rel="noreferrer"><GitHubIcon /> Building in public as @adarshnub <Icon name="diagonal" size={16} /></a></div>
      </div>
      <div className="profile-grid">
        <article><Icon name="command" size={27} /><span>Education</span><h3>Rajiv Gandhi Institute<br />of Technology</h3><p>Kottayam · 2018–2022</p></article>
        <article><Icon name="spark" size={27} /><span>Learning by doing</span><h3>The Odin Project</h3><p>Full stack development coursework, followed by a growing collection of personal projects and experiments.</p></article>
        <article><Icon name="cursor" size={27} /><span>Independent work</span><h3>Origin Studios</h3><p>Exploring desktop assistance with Autobots and creative tools through FrameOS and the Origin studio interface.</p></article>
      </div>
      <div className="developer-focus"><span className="eyebrow">WHAT I WORK WITH</span><div>{["React & Next.js", "TypeScript", "Python", "C# & Avalonia", "AI integrations", "Three.js & WebGL", "Supabase & PostgreSQL", "AWS"].map(item => <span key={item}>{item}</span>)}</div></div>
    </section>
    <section id="spotlight" className="section developer-spotlight" data-flight-section>
      <div className="section-heading"><span className="eyebrow">ON THE WORKBENCH</span><h2>Different ideas.<br /><span>The same urge to build.</span></h2><p>A closer look at three projects that connect intelligent tools with visible, understandable workflows.</p></div>
      <div className="spotlight-grid">
        <article className="spotlight-card spotlight-autobots"><div className="spotlight-label"><span>01 / DESKTOP INTELLIGENCE</span><Icon name="cursor" size={24} /></div><div className="spotlight-visual" aria-hidden="true"><span className="spotlight-command">One task. Step by step.</span><div className="spotlight-steps"><i /><i /><i /><span>YOU’RE IN CONTROL</span></div></div><h3>Autobots</h3><p>An assistant that works on your desktop while keeping its activity visible and a local STOP within reach.</p><a href="/">Meet the little helpers <Icon name="arrow" size={18} /></a></article>
        <article className="spotlight-card spotlight-frame"><div className="spotlight-label"><span>02 / CREATIVE INFRASTRUCTURE</span><Icon name="play" size={24} /></div><div className="spotlight-visual spotlight-timeline" aria-hidden="true"><div><i /><i /><i /></div><div><i /><i /></div><div><i /></div><span /></div><h3>FrameOS</h3><p>A structured editing engine that gives agents a way to plan, preview, and commit video edits through APIs.</p><a href={GITHUB + "/FrameOS"} target="_blank" rel="noreferrer">Inside the editing engine <Icon name="diagonal" size={18} /></a></article>
        <article className="spotlight-card spotlight-trace"><div className="spotlight-label"><span>03 / DEVELOPER EXPERIENCE</span><Icon name="command" size={24} /></div><div className="spotlight-visual spotlight-trace-lines" aria-hidden="true"><span>browser.error <i>captured</i></span><span>source.context <i>connected</i></span><span>patch.preview <i>ready for review</i></span></div><h3>Tracefy</h3><p>A debugging prototype that brings the browser, terminal, and code into a shared timeline for AI-assisted diagnosis.</p><a href={GITHUB + "/tracefy"} target="_blank" rel="noreferrer">Follow the trace <Icon name="diagonal" size={18} /></a></article>
      </div>
    </section>
    <section id="projects" className="section developer-projects" data-flight-section>
      <div className="section-heading"><span className="eyebrow">THE PROJECT SHELF</span><h2>A few rabbit holes.<br /><span>A lot of things built.</span></h2><p>Selected apps, libraries, prototypes, and experiments from my GitHub. Pick a category or follow a technology you’re curious about.</p></div>
      <ProjectGallery />
    </section>
    <section className="section developer-connect" data-flight-section><span className="eyebrow">IDEAS START WITH A HELLO</span><h2>Something on<br /><span>your mind?</span></h2><p>Developer tools, a product idea, or a wonderfully strange experiment.<br className="desktop-break" /> I’m always curious about what people are building.</p><div className="hero-actions"><a className="button primary" href={LINKEDIN} target="_blank" rel="noreferrer">Say hello on LinkedIn <Icon name="diagonal" size={18} /></a><a className="button secondary" href={GITHUB} target="_blank" rel="noreferrer"><GitHubIcon /> Follow the work</a></div></section>
  </main><Footer /></>;
}

createRoot(document.getElementById("root")!).render(<StrictMode><Developer /></StrictMode>);
