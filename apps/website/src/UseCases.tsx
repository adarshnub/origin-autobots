import { useState } from "react";
import { Icon } from "./ui";
import "./use-cases.css";

const workflows = [
  {
    id: "meet", icon: "mic", label: "Bring everyone together", title: "From “let’s talk” to being there.",
    apps: ["Google Meet", "WhatsApp"], status: "Workflow example",
    prompt: "Create a Google Meet now, join with my camera and microphone off, then send the link to Amal TGH on WhatsApp saying: ‘I’ve started the meeting. Join me using this link.’",
    steps: [
      ["Create the meeting", "Open Google Meet and get a new meeting link."],
      ["Join, on your terms", "Enter the meeting with your microphone and camera off."],
      ["Bring your friend in", "Find the specified WhatsApp contact and send the link with your invitation."],
      ["Check the result", "Confirm the message is visible and report whether you joined successfully."],
    ],
    note: "Requires signed-in accounts and a clearly identified contact. This complete meeting-and-message workflow has not yet passed a live test. Login prompts and waiting rooms may need your help.",
  },
  {
    id: "notes", icon: "window", label: "Turn a draft into a deliverable", title: "The little edits. All taken care of.",
    apps: ["Notepad", "Local files"], status: "Workflow example",
    prompt: "Open my launch checklist in Notepad, update its status, add the missing review step, save a separate final copy, then reopen it and check the contents.",
    steps: [
      ["Open the right draft", "Work inside the folder named in your instruction."],
      ["Make the requested edits", "Update the status and complete the checklist."],
      ["Save a separate copy", "Keep the original and give the final file a clear name."],
      ["Reopen and review", "Read the saved file back before reporting completion."],
    ],
    note: "Use a specific file and destination. Live qualification is in progress; an example prompt is not a guarantee of completion.",
  },
  {
    id: "plan", icon: "command", label: "Put tomorrow on the calendar", title: "Make the plan. Bring someone along.",
    apps: ["Google Calendar", "Google Meet", "WhatsApp"], status: "Workflow example",
    prompt: "Schedule a 15-minute Google Meet for tomorrow at 11 AM IST, then send Amal TGH the meeting link on WhatsApp with the date and time and ask him to join.",
    steps: [
      ["Set the date and time", "Create the event in your signed-in Google Calendar, using your stated time zone."],
      ["Add a place to meet", "Attach Google Meet and save the event."],
      ["Send the invitation", "Share the new meeting link, date and time with the specified WhatsApp contact."],
      ["Check both ends", "Confirm the saved event and the visible sent message before reporting completion."],
    ],
    note: "Include the time zone and an exact contact name. This complete scheduling-and-sharing workflow is undergoing live qualification; account or permission prompts may need your help.",
  },
];

export function UseCases() {
  const [selected, setSelected] = useState(0);
  const workflow = workflows[selected];
  return <section className="section use-cases-section" id="use-cases" data-flight-section>
    <div className="section-heading"><span className="eyebrow">START WITH SOMETHING YOU WANT DONE</span><h2>One instruction.<br /><span>A whole chain of little wins.</span></h2><p>Give Autobots the destination, the details, and the people involved. Follow the steps across your apps from one request.</p></div>
    <div className="use-cases-layout">
      <div className="use-case-picker" role="tablist" aria-label="Automation examples" aria-orientation="vertical">
        {workflows.map((item, index) => <button key={item.id} type="button" role="tab" id={`use-case-tab-${item.id}`} aria-controls="use-case-panel" aria-selected={selected === index} tabIndex={selected === index ? 0 : -1} onClick={() => setSelected(index)} onKeyDown={event => {
          const next = event.key === "ArrowDown" ? (index + 1) % workflows.length : event.key === "ArrowUp" ? (index + workflows.length - 1) % workflows.length : event.key === "Home" ? 0 : event.key === "End" ? workflows.length - 1 : null;
          if (next === null) return;
          event.preventDefault(); setSelected(next);
          document.getElementById(`use-case-tab-${workflows[next].id}`)?.focus();
        }}><span className="use-case-number">0{index + 1}</span><span>{item.label}</span><Icon name="arrow" size={17} /></button>)}
        <div className="use-case-aside"><Icon name="shield" size={18} /><p>One active task. A visible pilot.<br />Your STOP button is always nearby.</p></div>
      </div>
      <article className="use-case-panel" id="use-case-panel" role="tabpanel" aria-labelledby={`use-case-tab-${workflow.id}`} tabIndex={0}>
        <div className="use-case-meta"><div>{workflow.apps.map(app => <span key={app}>{app}</span>)}</div><span>{workflow.status}</span></div>
        <h3>{workflow.title}</h3>
        <blockquote><span className="use-case-quote-icon"><Icon name={workflow.icon} /></span><p>“{workflow.prompt}”</p></blockquote>
        <ol className="use-case-steps">{workflow.steps.map(([title, detail], index) => <li key={title}><span>{index + 1}</span><div><h4>{title}</h4><p>{detail}</p></div></li>)}</ol>
        <p className="use-case-note">{workflow.note}</p>
      </article>
    </div>
  </section>;
}
