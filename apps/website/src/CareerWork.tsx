import { Icon } from "./ui";

const contributions = [
  { name: "Infinite Nexus", type: "Collaborative AI filmmaking", text: "Contributed to a shared filmmaking canvas with AI video models, scene management, version history, and permission-based collaboration.", detail: "React Flow, Liveblocks, and a custom snapshot-and-index undo/redo system kept collaborative editing at the center of the experience." },
  { name: "AI Video Editor", type: "From timeline to export", text: "Built out a web video editor from an open-source foundation, adding clip editing, animations, transitions, captions, and editable text overlays.", detail: "Integrated ElevenLabs voice changing and Whisper captions, with SQS, dead-letter queues, workers, Lambda, and Remotion supporting the export pipeline." },
  { name: "Infinite Product Swap", type: "AI advertising tools", text: "Contributed to a tool for replacing products within ad videos, making it possible to create variations from an existing creative.", detail: "Worked on frame-by-frame generation and automated polygonal masking, alongside computer-vision tools including Grounding DINO." },
  { name: "Long-form AI Video", type: "Generation & continuity", text: "Contributed to a generation workflow that joins multiple AI-generated clips into longer videos.", detail: "Focused on maintaining visual and temporal consistency between short clips and connecting model output to the final composition." },
  { name: "Infinite Studios Marketplace", type: "Creators & brand collaboration", text: "Contributed to a social platform connecting AI content creators and brands.", detail: "Part of a broader body of product work spanning responsive interfaces, authentication, complex forms, and iterative feature delivery." },
  { name: "Teledesk", type: "Messaging & productivity", text: "Contributed to a Telegram-integrated task management system.", detail: "Connected messaging with task-oriented product workflows." },
  { name: "The Job Applicant Perspective", type: "Recruitment workflows", text: "Worked on a job posting platform with interfaces for HR teams and administrators.", detail: "Brought distinct user roles and hiring workflows into a shared web product." },
  { name: "Fintalent", type: "Hiring operations", text: "Contributed to a job posting and hiring-status management system.", detail: "Helped bring job publishing and candidate progress into an organized product experience." },
  { name: "Omnipacs", type: "Healthcare software", text: "Worked on a patient-record management system for Omni-router.", detail: "Contributed to the interfaces and workflows used to organize patient information." },
  { name: "Meeval", type: "Care & patient records", text: "Contributed to a records and prescription management system for cancer patients.", detail: "Worked on structured healthcare information and the associated product interfaces." },
];

export function CareerWork() {
  return <section className="section career-section" id="experience" data-flight-section>
    <div className="section-heading"><span className="eyebrow">BEYOND PERSONAL PROJECTS</span><h2>Products built.<br /><span>Experience earned.</span></h2><p>Selected professional contributions across AI filmmaking, collaborative tools, hiring, and healthcare.</p></div>
    <div className="career-timeline">
      <article><span className="career-year">MAY 2026 — NOW</span><h3>AI Engineer</h3><p>Ant Venture.ai</p><span className="career-current"><span className="status-dot" /> Current chapter</span></article>
      <article><span className="career-year">SEP 2025 — MAY 2026</span><h3>Software Development Engineer 2</h3><p>TGH Technologies · Kochi</p><small>AI video systems, collaborative editing, and scalable media processing.</small></article>
      <article><span className="career-year">JAN 2024 — AUG 2025</span><h3>Frontend Developer</h3><p>TGH Technologies</p><small>Full-time from May 2024. React and Next.js products, authentication, forms, and animation.</small></article>
      <article><span className="career-year">SEP 2023</span><h3>React JS Intern</h3><p>The first professional chapter</p><small>A foundation in building interfaces, followed by full stack product work.</small></article>
    </div>
    <div className="career-work-grid">{contributions.map((work, i) => <article key={work.name} className="career-work"><div><span>{String(i + 1).padStart(2, "0")}</span><Icon name={i < 4 ? "play" : "window"} size={19} /></div><span className="career-type">{work.type}</span><h3>{work.name}</h3><p>{work.text}</p><p className="career-detail">{work.detail}</p></article>)}</div>
  </section>;
}
