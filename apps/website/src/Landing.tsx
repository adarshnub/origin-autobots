import { useEffect, useRef, useState } from "react";
import { BotsScene, useFlight } from "./BotsScene";
import { Header, Footer, Icon, LINKEDIN } from "./ui";
import home from "./assets/app-home.png";
import working from "./assets/app-working.png";
import settings from "./assets/app-settings.png";
import pilot from "./assets/pilot-working.png";

const downloadUrl = import.meta.env.VITE_WINDOWS_DOWNLOAD_URL as string | undefined;
const screens = [
  { title: "Give it a task", image: home, description: "A simple starting point for whatever is next.", caption: "Actual Windows app · task composer" },
  { title: "Follow the action", image: working, description: "Each step has a place in the activity feed.", caption: "Actual Windows app · sample task in UI preview mode" },
  { title: "Make it yours", image: settings, description: "Your pace. Your task limits. Your preferences.", caption: "Actual Windows app · settings preview" },
];

function ProductGallery() {
  const [selected,setSelected] = useState(0);
  const [playing,setPlaying] = useState(false);
  const dialog = useRef<HTMLDialogElement>(null);
  const screen = screens[selected];
  useEffect(() => {
    if (!playing) return;
    const timer = window.setInterval(() => setSelected((value) => (value + 1) % screens.length),3500);
    return () => clearInterval(timer);
  },[playing]);
  return <div className="gallery">
    <div className="gallery-toolbar"><div className="gallery-tabs" aria-label="Choose an app screenshot">{screens.map((item,i) => <button key={item.title} type="button" aria-pressed={selected === i} className={selected === i ? "active" : ""} onClick={() => {setSelected(i);setPlaying(false);}}><span>0{i + 1}</span>{item.title}</button>)}</div><button className="gallery-play" type="button" onClick={() => setPlaying(!playing)}><Icon name={playing ? "pause" : "play"} size={17} /><span>{playing ? "Pause" : "Play walkthrough"}</span></button></div>
    <figure className="gallery-figure"><div className="gallery-image-wrap"><img key={screen.image} src={screen.image} alt={screen.caption} width="1122" height="831" loading="lazy" /><button className="expand-button" type="button" aria-label="Expand app screenshot" onClick={() => { setPlaying(false);dialog.current?.showModal(); }}><Icon name="expand" /></button></div><figcaption><span>{screen.description}</span><small>{screen.caption}</small></figcaption></figure>
    <dialog className="screenshot-dialog" ref={dialog} onClick={(event) => {if(event.target === event.currentTarget)dialog.current?.close();}}><button className="dialog-close" type="button" aria-label="Close screenshot" onClick={() => dialog.current?.close()}><Icon name="close" /></button><img src={screen.image} alt={screen.caption} /><p>{screen.caption}</p></dialog>
  </div>;
}

export default function Landing() {
  const motion = useFlight();
  return <><BotsScene motion={motion} /><Header /><main id="top">
    <section className="hero" data-flight-section>
      <div className="hero-atmosphere" aria-hidden="true" /><div className="hero-copy"><span className="eyebrow-pill"><span className="status-dot" /> Meet your desktop's new sidekick <Icon name="spark" size={14} /></span><h1>A little help.<br /><span>A lot more possible.</span></h1><p>Meet Autobots. Tell it what needs doing, and watch your<br className="desktop-break" /> Windows desktop get to work. You stay in charge.</p><div className="hero-actions"><a className="button primary" href="#download"><Icon name="windows" size={17} /> Get the Windows pilot <Icon name="arrow" size={17} /></a><a className="button secondary" href="#in-action"><Icon name="play" size={17} /> See it in action</a></div><div className="hero-small"><span /> Built for Windows 11 <span className="small-divider">·</span> Private owner pilot</div></div>
      <div className="hero-product"><div className="product-halo" aria-hidden="true" /><div className="floating-prompt"><span className="prompt-icon"><Icon name="spark" /></span><div><small>You bring the idea.</small><strong>“Draft my to-do list in Notepad.”</strong></div><span className="prompt-enter">↵</span></div><div className="app-frame"><div className="frame-chrome"><div><i /><i /><i /></div><span>YOUR DESKTOP, A LITTLE MORE CAPABLE</span><Icon name="window" size={14} /></div><img src={home} alt="The actual Autobots Windows app, with its task composer, activity feed and controls" width="1122" height="831" fetchPriority="high" /></div><div className="floating-complete"><span className="check-circle"><Icon name="check" size={16} /></span><div><strong>Every move, in view.</strong><small>A real pointer. A visible task.</small></div></div></div>
      <div className="hero-bottom"><span>Small characters. Big desktop energy.</span><a href="#product">Follow the bots <span>↓</span></a><span className="hero-hint">Psst. They follow your cursor.</span></div>
    </section>

    <section className="section meet-section" id="product" data-flight-section><div className="section-heading"><span className="eyebrow">YOUR WORDS. REAL WORK.</span><h2>A teammate for the<br /><span>things between the things.</span></h2><p>Opening apps. Writing a quick note. Moving from one window to the next.<br className="desktop-break" /> Autobots turns a clear instruction into visible desktop steps.</p></div><div className="feature-grid"><article className="feature-card"><div className="feature-icon violet"><Icon name="command" size={27} /></div><h3>Just say what’s next.</h3><p>Describe a task in plain language. Your instruction sets the scope, and Autobots works within it.</p><div className="mini-prompt"><span>→</span> Open Notepad and write a plan<span className="typing-caret" /></div></article><article className="feature-card"><div className="feature-icon blue"><Icon name="cursor" size={27} /></div><h3>Watch the work happen.</h3><p>A visible cursor and a floating pilot bar keep each action in view while your desktop does its thing.</p><img className="mini-pilot" src={pilot} alt="Autobots pilot bar showing a sample clicking action and the Stop button" width="680" height="94" loading="lazy" /></article><article className="feature-card"><div className="feature-icon peach"><Icon name="shield" size={27} /></div><h3>You have the final say.</h3><p>Set limits for every run. Stop locally at any time, even if the internet connection drops.</p><div className="shortcut"><kbd>Ctrl</kbd><span>+</span><kbd>Alt</kbd><span>+</span><kbd>Shift</kbd><span>+</span><kbd>S</kbd></div></article></div><p className="under-note">Windows first. Works through the visible screen of supported, normal-permission apps.</p></section>

    <section className="section action-section" id="in-action" data-flight-section><div className="section-heading"><span className="eyebrow">LESS IMAGINING. MORE SEEING.</span><h2>Here’s the app.<br /><span>And a look inside the action.</span></h2><p>Explore the real Windows interface, from the first instruction<br className="desktop-break" /> to the activity feed and the controls that keep it yours.</p></div><ProductGallery /><p className="under-note">Screenshots from the desktop app. Activity shown is a sample UI preview.</p></section>

    <section className="section how-section" id="how" data-flight-section><div className="how-layout"><div className="how-copy"><span className="eyebrow">GET INTO YOUR FLOW</span><h2>One task.<br /><span>A little magic.</span></h2><p>No elaborate workflow to draw. Just a clear goal and a view of what happens next.</p><div className="pilot-display"><div className="pilot-display-label"><span className="status-dot" /> YOUR CO-PILOT, ALWAYS IN REACH</div><img src={pilot} alt="Actual floating pilot bar in preview mode with a visible Stop button" width="680" height="94" loading="lazy" /><small>Floating pilot bar · sample action preview</small></div></div><ol className="how-steps"><li><span>01</span><div><h3>Make yourself at home.</h3><p>Extract your Windows pilot ZIP, open Autobots, and connect your owner account.</p></div></li><li><span>02</span><div><h3>Give it something to do.</h3><p>Type your task and choose Start. Your instruction grants that task its bounded run.</p></div></li><li><span>03</span><div><h3>Follow the little moves.</h3><p>Autobots observes, acts, and looks again. Each step appears in the live activity feed.</p></div></li><li><span>04</span><div><h3>Stop whenever you want.</h3><p>Use the pilot bar or Ctrl + Alt + Shift + S. Stopping prevents future input; completed actions stay completed.</p></div></li></ol></div></section>

    <section className="section download-section" id="download" data-flight-section><div className="download-card"><div className="download-copy"><span className="eyebrow">A NEW LITTLE TEAMMATE</span><h2>Make room for<br /><span>more possible.</span></h2><p>Autobots starts on Windows.<br />Your desktop adventure starts here.</p><a className="button primary" href={downloadUrl || LINKEDIN} target={downloadUrl ? undefined : "_blank"} rel={downloadUrl ? undefined : "noreferrer"}><Icon name="windows" size={18} /> {downloadUrl ? "Download for Windows" : "Get the Windows pilot"}<Icon name="arrow" size={18} /></a><small>{downloadUrl ? "Windows 11 · x64 · pilot release" : "Private pilot. Request access from the developer."}</small></div><div className="download-orbit" aria-hidden="true"><div /><div /><div /><span>Hello, human.</span></div><div className="platform-strip"><div><Icon name="windows" /><strong>Windows</strong><span className="available-tag">Private pilot</span></div><div><span className="platform-letter">⌘</span><strong>macOS</strong><span>Coming soon</span></div><div><span className="platform-letter">&gt;_</span><strong>Linux</strong><span>Coming soon</span></div></div></div></section>

    <section className="section maker-section" data-flight-section><div className="maker-card"><div className="maker-avatar" aria-hidden="true">av<span>✳</span></div><div><span className="eyebrow">BUILT BY A HUMAN, FOR HUMANS</span><h2>Meet Adarsh.</h2><p>The developer behind Autobots by Origin Studios.<br />A little curiosity. A lot of building.</p></div><a className="button secondary" href="/developer/index.html">Meet the maker <Icon name="diagonal" size={18} /></a></div></section>
  </main><Footer /></>;
}
