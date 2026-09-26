import { useEffect, useRef, useState } from "react";
import type { CSSProperties, ReactNode } from "react";
import { EyesLogo, Icon } from "./ui";
import companion from "./assets/bot-companion.png";
import "./walkthrough.css";

const request = "Create a Google Calendar meeting for Tuesday at 3 PM, add a Google Meet link, and send it to Amal TGH on WhatsApp.";
const meetingLink = "meet.google.com/demo-preview";
const message = "Hi Amal! Let’s catch up on Tuesday, 29 September at 3:00 PM IST. Here’s the Google Meet link: " + meetingLink;
const steps = [
  { name: "Say the task", app: "Autobots", title: "Listening to your request", detail: "One voice instruction. A clear time, recipient, and goal.", duration: 6500 },
  { name: "Open browser", app: "Browser", title: "Opening Google Calendar", detail: "Autobots moves from your desktop into the browser.", duration: 3500 },
  { name: "Create meeting", app: "Google Calendar", title: "Creating the meeting", detail: "Add the title, time, and a Google Meet conference.", duration: 6000 },
  { name: "Get the link", app: "Google Calendar", title: "Meeting created. Link ready.", detail: "Save the event and carry its meeting link to the next app.", duration: 4000 },
  { name: "Find Amal", app: "WhatsApp", title: "Finding Amal TGH", detail: "Open WhatsApp and find the contact named in your request.", duration: 3500 },
  { name: "Share it", app: "WhatsApp", title: "Sharing the meeting link", detail: "Compose the requested message and share the meeting details.", duration: 6500 },
  { name: "All done", app: "Autobots", title: "From a thought to a plan.", detail: "The walkthrough ends with a saved meeting and a shared link.", duration: 1 },
];

export function AppPreview({ listening = false, transcript = "", compact = false }: { listening?: boolean; transcript?: string; compact?: boolean }) {
  return <div className={"app-replica " + (compact ? "compact" : "")}>
    <div className="replica-titlebar"><div><EyesLogo /><strong>Autobots</strong><span>PILOT</span></div><div className="replica-window-tools" aria-hidden="true">─ <span>□</span> ×</div></div>
    <div className="replica-content"><div className="replica-greeting"><span>YOUR DESKTOP, A LITTLE MORE CAPABLE</span><h3>What can I do for you?</h3><p>Say the task. Follow every move. Stay in control.</p></div>
      <div className={"replica-composer " + (listening ? "is-listening" : "")}><div className="replica-composer-head"><EyesLogo /><span>{listening ? "Listening to your voice…" : "A little help starts here"}</span><span className="replica-shortcut">Ctrl + Alt + Space</span></div><p>{transcript || "Create a meeting and share the link with Amal…"}<span className="replica-caret" /></p><div className="replica-composer-bottom"><span><Icon name="shield" size={13} /> 40 steps · 10 minutes</span><div><span className="replica-mic"><Icon name="mic" size={16} /></span><span className="replica-start"><Icon name={listening ? "mic" : "play"} size={13} /> {listening ? "Listening" : "Start task"}</span></div></div></div>
      <div className="replica-suggestions"><span>Plan a meeting</span><span>Write a quick note</span><span>Organize my day</span></div>
      <div className="replica-lower"><div className="replica-activity"><div><Icon name="spark" size={14} /><strong>Live activity</strong><span>Ready when you are</span></div><img src={companion} alt="" /><p>Your little helper is ready.</p><small>Each step appears here as your task unfolds.</small></div><div className="replica-guide"><strong>In your corner.</strong>{[["cursor", "Visible actions", "Watch the pointer move."], ["mic", "Talk naturally", "One instruction to get started."], ["shield", "Stop locally", "The controls stay with you."]].map(([icon,title,copy]) => <div key={title}><Icon name={icon} size={17} /><span><b>{title}</b><small>{copy}</small></span></div>)}</div></div>
      <div className="replica-status"><span><i /> Windows pilot</span><span>Origin Studios <span aria-hidden="true">✦</span></span></div>
    </div>
  </div>;
}

export function PilotPreview() {
  return <div className="static-pilot"><EyesLogo /><div><strong>Your co-pilot is right here.</strong><span>Opening Google Calendar…</span></div><small>Step 2</small><span className="static-stop">■ Stop</span></div>;
}

function BrowserChrome({ calendar, children }: { calendar: boolean; children: ReactNode }) {
  return <div className="demo-browser"><div className="browser-tabs"><span><i className={calendar ? "calendar-tab-icon" : "chrome-tab-icon"}>{calendar ? "29" : ""}</i>{calendar ? "Google Calendar" : "New tab"}<b>×</b></span><i>+</i><div>─　□　×</div></div><div className="browser-address"><span>←　→　↻</span><div><Icon name="shield" size={12} /><span>{calendar ? "calendar.google.com/calendar/u/0/r/week" : "Search or enter web address"}</span><span>☆</span></div><i>A</i></div>{children}</div>;
}

function CalendarScreen({ saved, progress }: { saved: boolean; progress: number }) {
  const title = "Project catch-up".slice(0, saved ? undefined : Math.max(1, Math.floor(progress * 55)));
  return <div className="calendar-app">
    <div className="calendar-header"><Icon name="menu" size={18} /><span className="google-calendar-logo">29</span><strong>Calendar</strong><span className="cal-today">Today</span><span className="cal-arrows">‹　›</span><b>September 2026</b><span className="cal-week">Week⌄</span><span className="cal-avatar">A</span></div>
    <div className="calendar-layout"><aside className="calendar-sidebar"><div className="calendar-create"><span>＋</span> Create⌄</div><strong>September 2026 <span>‹　›</span></strong><div className="mini-calendar">{"SMTWTFS".split("").map((day,i) => <span key={"d"+i}>{day}</span>)}{Array.from({length:35},(_,i) => <span key={i} className={i === 30 ? "selected" : ""}>{i < 2 ? 30+i : i > 31 ? i-31 : i-1}</span>)}</div><p>My calendars　⌃</p><span className="calendar-check">✓ <span>Adarsh</span></span><span className="calendar-check violet-check">✓ <span>Reminders</span></span></aside>
      <div className="calendar-week-grid"><div className="calendar-day-row"><small>GMT+05:30</small>{["MON 28","TUE 29","WED 30","THU 1","FRI 2"].map((day,i) => <div key={day}><span>{day.split(" ")[0]}</span><strong className={i === 1 ? "day-current" : ""}>{day.split(" ")[1]}</strong></div>)}</div><div className="calendar-hours">{["12 PM","1 PM","2 PM","3 PM","4 PM"].map(time => <div key={time}><span>{time}</span><i /><i /><i /><i /><i /></div>)}{saved && <div className="calendar-event"><strong>Project catch-up</strong><span>3 – 3:30pm</span><small>Google Meet</small></div>}</div></div>
    </div>
    <div className={"calendar-editor " + (saved ? "saved-event" : "")}><div className="calendar-editor-top"><span>{saved ? "✓ Event saved" : "New event"}</span><span>×</span></div><h4>{title}<span className={!saved && progress < .4 ? "typing-caret" : ""} /></h4><div className="cal-event-kind">Event</div><div className="event-field"><span>◷</span><div><strong>Tuesday, September 29</strong><p>3:00 PM – 3:30 PM <small>India Standard Time</small></p></div></div><div className="event-field"><span className="meet-symbol">▰</span><div><strong>{saved || progress > .42 ? "Join with Google Meet" : "Add Google Meet video conferencing"}</strong>{(saved || progress > .42) && <p className="sample-meet">{meetingLink}<span aria-hidden="true"> ▢</span></p>}</div></div><div className="event-field event-description"><Icon name="menu" size={14} /><span>Project catch-up with Amal</span></div><div className="calendar-editor-footer"><span>{saved ? "Meeting details ready to share" : "Busy · Default visibility"}</span><span className="calendar-save">{saved ? "Copy link" : "Save"}</span></div></div>
    {saved && <div className="calendar-toast"><Icon name="check" size={15} /> Event saved <span>Sample meeting</span></div>}
  </div>;
}

function WhatsAppScreen({ step, progress }: { step: number; progress: number }) {
  const sent = step === 6 || step === 5 && progress > .72;
  const typed = step >= 5 ? message.slice(0, sent ? undefined : Math.floor(progress / .6 * message.length)) : "";
  return <div className="whatsapp-app"><aside className="wa-rail"><span className="wa-appmark">◉</span><span>◌</span><span>☷</span><span>▧</span><span className="wa-bottom">⚙</span><i>A</i></aside><aside className="wa-chats"><div><h4>Chats</h4><span>⊞　⋮</span></div><div className="wa-search"><span>⌕</span>{step === 4 ? "Amal TGH".slice(0,Math.max(1,Math.floor(progress*30))) : "Amal TGH"}</div><div className="wa-filter"><span>All</span><span>Unread</span><span>Groups</span></div><div className="wa-contact"><span className="wa-avatar">AT</span><div><strong>Amal TGH</strong><p>{sent ? "✓✓ Hi Amal! Let’s catch up…" : "Click to open conversation"}</p></div><small>{sent ? "Now" : ""}</small></div><p className="wa-sample-note">Demo conversation</p></aside><div className="wa-conversation"><div className="wa-chat-header"><span className="wa-avatar">AT</span><div><strong>Amal TGH</strong><small>Example contact</small></div><span>⌕　⋮</span></div><div className="wa-message-area"><span className="wa-date">TODAY</span><span className="wa-encryption">Messages in this walkthrough are simulated.</span>{sent && <div className="wa-bubble"><div className="wa-link-preview"><span className="meet-symbol">▰</span><div><strong>Google Meet</strong><p>Project catch-up · Tue, 29 Sep · 3 PM</p></div></div><p>Hi Amal! Let’s catch up on Tuesday, 29 September at 3:00 PM IST. Here’s the Google Meet link:</p><span className="wa-meet-link">{meetingLink}</span><small>2:42 PM <span>✓✓</span></small></div>}</div><div className="wa-composer"><span>＋</span><span>☺</span><p>{sent ? "Type a message" : typed || "Type a message"}{!sent && typed && <span className="typing-caret" />}</p><span className={typed && !sent ? "wa-send" : ""}>{typed && !sent ? "➤" : <Icon name="mic" size={18} />}</span></div></div></div>;
}

export function TaskWalkthrough() {
  const [step, setStep] = useState(0);
  const [elapsed, setElapsed] = useState(0);
  const [playing, setPlaying] = useState(false);
  const [stopped, setStopped] = useState(false);
  const [inView, setInView] = useState(false);
  const host = useRef<HTMLDivElement>(null);
  const autoplayHandled = useRef(false);
  const current = steps[step];
  const progress = Math.min(1, elapsed / current.duration);

  useEffect(() => {
    const observer = new IntersectionObserver(entries => {
      const entry = entries[0];
      setInView(entry.isIntersecting && entry.intersectionRatio >= .15);
    }, { threshold: .15 });
    if (host.current) observer.observe(host.current);
    return () => observer.disconnect();
  }, []);
  useEffect(() => {
    const startOnEntry = () => {
      if (!inView || document.hidden || autoplayHandled.current) return;
      autoplayHandled.current = true;
      if (!window.matchMedia("(prefers-reduced-motion: reduce)").matches) setPlaying(true);
    };
    startOnEntry();
    document.addEventListener("visibilitychange", startOnEntry);
    return () => document.removeEventListener("visibilitychange", startOnEntry);
  }, [inView]);
  useEffect(() => {
    window.dispatchEvent(new CustomEvent("autobots:demo", { detail: inView }));
    return () => { window.dispatchEvent(new CustomEvent("autobots:demo", { detail: false })); };
  }, [inView]);
  useEffect(() => {
    if (!playing || !inView) return;
    const onHidden = () => { if (document.hidden) setPlaying(false); };
    document.addEventListener("visibilitychange", onHidden);
    const timer = window.setInterval(() => setElapsed(value => value + 80), 80);
    return () => { clearInterval(timer); document.removeEventListener("visibilitychange", onHidden); };
  }, [playing, inView]);
  useEffect(() => {
    if (!playing || elapsed < current.duration) return;
    if (step >= steps.length - 1) { setPlaying(false); return; }
    setStep(value => value + 1); setElapsed(0);
  }, [elapsed, playing, step, current.duration]);

  const select = (index: number) => { autoplayHandled.current = true; setStep(index); setElapsed(steps[index].duration * .96); setPlaying(false); setStopped(false); };
  const play = () => {
    autoplayHandled.current = true;
    if (step === steps.length - 1 || stopped) { setStep(0); setElapsed(0); }
    else if (!playing && elapsed >= current.duration * .95) setElapsed(0);
    setStopped(false); setPlaying(value => !value);
  };
  const stop = () => { autoplayHandled.current = true; setPlaying(false); setStopped(true); };
  const coordinates = step === 0 ? [77,58] : step === 1 ? [48,15] : step === 2 ? (progress < .4 ? [59,36] : progress < .76 ? [65,53] : [78,72]) : step === 3 ? [78,72] : step === 4 ? [23,22] : progress < .72 ? [62,77] : [92,78];

  return <div className="task-demo" ref={host}>
    <div className="demo-topline"><span><i /> VOICE → CALENDAR → WHATSAPP</span><span className="demo-disclosure">Interactive simulation</span></div>
    <div className={"demo-desktop " + (playing ? "demo-playing" : "demo-paused")}>
      <div className="demo-scene" key={step}>
        {step === 0 ? <div className="demo-app-home"><AppPreview listening={playing} transcript={playing || elapsed > 0 ? request.slice(0, Math.floor(progress * request.length * 1.3)) : undefined} compact />{(playing || elapsed > 0) && <div className="voice-transcript"><div className="voice-wave" aria-hidden="true">{Array.from({length:28},(_,i) => <i key={i} style={{"--bar": (Math.sin(i*2.3)*.5+.5)*22+6+"px", "--delay": i*-.13+"s"} as CSSProperties} />)}</div><span>{progress > .8 ? "Voice instruction ready. Starting your task…" : "You speak. Autobots listens."}</span></div>}</div> : step === 1 ? <BrowserChrome calendar={progress > .55}><div className="browser-new-tab"><div className="google-wordmark"><span>G</span><span>o</span><span>o</span><span>g</span><span>l</span><span>e</span></div><div className="newtab-search">⌕ <span>Google Calendar</span><Icon name="mic" size={16} /></div><div className="newtab-shortcut"><span className="google-calendar-logo">29</span><small>Calendar</small></div></div></BrowserChrome> : step < 4 ? <BrowserChrome calendar><CalendarScreen saved={step === 3} progress={progress} /></BrowserChrome> : <WhatsAppScreen step={step} progress={progress} />}
      </div>
      {step > 0 && step < 6 && <div className="demo-cursor" aria-hidden="true" style={{left: coordinates[0]+"%",top: coordinates[1]+"%"}}><svg viewBox="0 0 24 30" width="24" height="30"><path d="M3 2v23l6-6 5 9 4-2-5-9h9Z" fill="#b3a3ff" stroke="#fff" strokeWidth="1.5" /></svg><span><EyesLogo /> Autobots</span></div>}
      <div className={"demo-pilot " + (stopped ? "pilot-stopped" : "")}><EyesLogo /><div className="demo-pilot-copy"><strong>{stopped ? "Demo stopped" : step === 6 ? "Task complete" : current.title}</strong><span>{stopped ? "You stopped the preview. Replay whenever you're ready." : step === 0 ? playing ? "Listening · voice task" : "Press play to watch your request unfold" : step === 6 ? "Meeting saved · link shared with Amal TGH" : current.app + " · " + (playing ? "Working through your task" : "Preview paused")}</span></div><span className="demo-step-count">{step === 6 ? "✓" : (step+1) + " / 7"}</span>{step === 6 || stopped ? <button type="button" onClick={() => {setStep(0);setElapsed(0);setStopped(false);setPlaying(true);}}><Icon name="play" size={14} /> Replay</button> : <button type="button" className="pilot-stop" onClick={stop}>■ Stop</button>}</div>
      <div className="demo-taskbar" aria-hidden="true"><Icon name="windows" size={16} /><div>⌕ Search</div><i className="chrome-task-icon" /><span className="taskbar-wa">◉</span><EyesLogo /><span className="taskbar-time">2:42 PM<br />29/09/2026</span></div>
      {step === 6 && <div className="demo-complete"><img src={companion} alt="" /><div><span className="eyebrow">NICELY DONE, LITTLE HELPER.</span><h3>Meeting made.<br />Link shared.</h3><p>One voice request, across your apps.</p><span><Icon name="check" size={14} /> Google Calendar <Icon name="check" size={14} /> WhatsApp</span></div></div>}
    </div>
    <div className="demo-playback"><button className="demo-play-button" type="button" onClick={play} aria-label={playing ? "Pause simulated walkthrough" : "Play simulated walkthrough"}><Icon name={playing ? "pause" : "play"} size={18} /> {playing ? "Pause" : step === 6 || stopped ? "Replay demo" : "Play voice demo"}</button><div className="demo-current" role="status" aria-live="polite"><strong>{current.name}</strong><span>{current.detail}</span></div><span className="demo-duration">{String(step+1).padStart(2,"0")} / 07</span></div>
    <div className="demo-chapters" role="group" aria-label="Walkthrough chapters">{steps.map((item,index) => <button key={item.name} type="button" aria-pressed={step === index} onClick={() => select(index)}><span className="chapter-track"><i style={{width: (index < step ? 100 : index === step ? progress*100 : 0)+"%"}} /></span><span>{index+1}. {item.name}</span></button>)}</div>
    <p className="demo-honesty">Illustrative app screens and sample meeting details. This interactive demo does not record audio, create meetings, or send messages.</p>
  </div>;
}

export function BotFormationControls() {
  const [formation,setFormation] = useState(0);
  return <div className="formation-controls"><span>Give the little crew a direction</span><div>{["Explore", "Orbit", "V formation"].map((name,index) => <button type="button" key={name} aria-pressed={formation === index} onClick={() => {setFormation(index);window.dispatchEvent(new CustomEvent("autobots:formation",{detail:index}));}}>{name}<span>{index === 0 ? "↝" : index === 1 ? "◌" : "⋁"}</span></button>)}</div></div>;
}
