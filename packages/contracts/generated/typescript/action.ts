export type PointerButton = "left" | "middle" | "right";

export type ProposedAction =
  | { kind: "click"; x: number; y: number; button: PointerButton; clicks?: 1 | 2 | 3 }
  | { kind: "type_text"; text: string; press_enter?: boolean }
  | { kind: "key_press"; key: string }
  | { kind: "scroll"; x: number; y: number; delta_x: number; delta_y: number }
  | { kind: "wait"; duration_ms: number }
  | { kind: "move"; x: number; y: number }
  | { kind: "drag"; x: number; y: number; to_x: number; to_y: number };

export interface ActionEnvelope {
  schema_version: 1;
  task_id: string;
  device_id: string;
  action_id: string;
  observation_id: string;
  lease_id: string;
  epoch: number;
  sequence: number;
  action: ProposedAction;
}
