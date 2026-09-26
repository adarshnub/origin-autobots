import { BotsScene, useFlight } from "./BotsScene";
import { Header, Footer, Icon, LINKEDIN } from "./ui";
import { AppPreview, TaskWalkthrough, PilotPreview, BotFormationControls } from "./TaskWalkthrough";
import { UseCases } from "./UseCases";

const downloadUrl = import.meta.env.VITE_WINDOWS_DOWNLOAD_URL as string | undefined;

export default function Landing() {
  const motion = useFlight();
  return <><BotsScene motion={motion} /><Header /><main id="top">
    <section className="hero" data-flight-section>
      <div className="hero-atmosphere" aria-hidden="true" />
      <div className="hero-copy"><span className="eyebrow-pill"><span className="status-dot" /> Meet your desktop's new sidekick <Icon name="spark" size={14} /></span><h1>A little help.<br /><span>A lot more possible.</span></h1><p>Meet Autobots. Tell it what needs doing, and watch your<br className="desktop-break" /> Windows desktop get to work. You stay in charge.</p><div className="hero-actions"><a className="button primary" href="#download"><Icon name="windows" size={17} /> Get the Windows pilot <Icon name="arrow" size={17} /></a><a className="button secondary" href="#in-action"><Icon name="play" size={17} /> Watch the voice demo</a></div><div className="hero-small"><span /> Built for Windows 11 <span className="small-divider">·</span> Private owner pilot</div></div>
      <div className="hero-product"><div className="product-halo" aria-hidden="true" /><div className="floating-prompt"><span className="prompt-icon"><Icon name="mic" /></span><div><small>You bring the idea.</small><strong>“Create a meeting. Send Amal the link.”</strong></div><span className="prompt-enter">↵</span></div><AppPreview /><div className="floating-complete"><span className="check-circle"><Icon name="check" size={16} /></span><div><strong>Your idea. In motion.</strong><small>Calendar → WhatsApp → done.</small></div></div></div>
      <div className="hero-bottom"><span>Small characters. Big desktop energy.</span><a href="#product">Follow the bots <span>↓</span></a><span className="hero-hint">Interface preview · Move your cursor.</span></div>
    </section>

    <section className="section meet-section" id="product" data-flight-section>
      <div className="section-heading"><span className="eyebrow">YOUR WORDS. REAL WORK.</span><h2>A teammate for the<br /><span>things between the things.</span></h2><p>Opening apps. Planning a meeting. Bringing the right people in.<br className="desktop-break" /> Autobots turns a clear instruction into visible desktop steps.</p></div>
      <div className="feature-grid">
        <article className="feature-card"><div className="feature-icon violet"><Icon name="mic" size={27} /></div><h3>Just say what’s next.</h3><p>Speak your task or type it out. See the transcript, then let your instruction set the scope of the run.</p><div className="mini-prompt"><span>→</span> Create a meeting and share the link<span className="typing-caret" /></div></article>
        <article className="feature-card"><div className="feature-icon blue"><Icon name="cursor" size={27} /></div><h3>Watch the work happen.</h3><p>A visible cursor and a floating pilot keep each action in view as Autobots moves between your apps.</p><div className="mini-pilot-new"><PilotPreview /></div></article>
        <article className="feature-card"><div className="feature-icon peach"><Icon name="shield" size={27} /></div><h3>You have the final say.</h3><p>Set limits for every run. Stop locally at any time, even if the internet connection drops.</p><div className="shortcut"><kbd>Ctrl</kbd><span>+</span><kbd>Alt</kbd><span>+</span><kbd>Shift</kbd><span>+</span><kbd>S</kbd></div></article>
      </div><p className="under-note">Windows first. Works through the visible screen of supported, normal-permission apps.</p>
    </section>

    <section className="section action-section" id="in-action" data-flight-section>
      <div className="section-heading"><span className="eyebrow">ONE REQUEST. A FEW LITTLE MOVES.</span><h2>Say it once.<br /><span>Watch the plan come together.</span></h2><p>“Create a Google Calendar meeting for Tuesday at 3 PM,<br className="desktop-break" /> and send the Meet link to Amal TGH on WhatsApp.”</p></div>
      <TaskWalkthrough />
    </section>

    <UseCases />

    <section className="section how-section" id="how" data-flight-section><div className="how-layout"><div className="how-copy"><span className="eyebrow">GET INTO YOUR FLOW</span><h2>One task.<br /><span>A little magic.</span></h2><p>No elaborate workflow to draw. Just a clear goal and a view of what happens next.</p><div className="pilot-display"><div className="pilot-display-label"><span className="status-dot" /> YOUR CO-PILOT, ALWAYS IN REACH</div><PilotPreview /><small>Floating pilot · interface preview</small></div></div><ol className="how-steps"><li><span>01</span><div><h3>Make yourself at home.</h3><p>Extract your Windows pilot ZIP, open Autobots, and connect your owner account.</p></div></li><li><span>02</span><div><h3>Give it something to do.</h3><p>Press Ctrl + Alt + Space to speak, or type your task. Review the voice transcript before the run begins.</p></div></li><li><span>03</span><div><h3>Follow the little moves.</h3><p>Autobots observes, acts, and looks again. The floating pilot stays nearby while each step appears in the activity feed.</p></div></li><li><span>04</span><div><h3>Stop whenever you want.</h3><p>Use the pilot bar or Ctrl + Alt + Shift + S. Stopping prevents future input; completed actions stay completed.</p></div></li></ol></div></section>

    <section className="section download-section" id="download" data-flight-section><div className="download-card"><div className="download-copy"><span className="eyebrow">A NEW LITTLE TEAMMATE</span><h2>Make room for<br /><span>more possible.</span></h2><p>Autobots starts on Windows.<br />Your desktop adventure starts here.</p><a className="button primary" href={downloadUrl || LINKEDIN} target={downloadUrl ? undefined : "_blank"} rel={downloadUrl ? undefined : "noreferrer"}><Icon name="windows" size={18} /> {downloadUrl ? "Download for Windows" : "Get the Windows pilot"}<Icon name="arrow" size={18} /></a><small>{downloadUrl ? "Windows 11 · x64 · pilot release" : "Private pilot. Request access from the developer."}</small></div><div className="download-orbit" aria-hidden="true"><div /><div /><div /><span>Hello, human.</span></div><div className="platform-strip"><div><Icon name="windows" /><strong>Windows</strong><span className="available-tag">Private pilot</span></div><div><span className="platform-letter">⌘</span><strong>macOS</strong><span>Coming soon</span></div><div><span className="platform-letter">&gt;_</span><strong>Linux</strong><span>Coming soon</span></div></div></div><BotFormationControls /></section>

    <section className="section maker-section" data-flight-section><div className="maker-card"><div className="maker-avatar" aria-hidden="true">av<span>✳</span></div><div><span className="eyebrow">BUILT BY A HUMAN, FOR HUMANS</span><h2>Meet Adarsh.</h2><p>AI Engineer. Full stack builder.<br />The human behind Autobots by Origin Studios.</p></div><a className="button secondary" href="/developer/index.html">Meet the maker <Icon name="diagonal" size={18} /></a></div></section>
  </main><Footer /></>;
}
