export type PointerButton = "left" | "middle" | "right";

export type ProposedAction =
  | { kind: "click"; x: number; y: number; button: PointerButton }
  | { kind: "type_text"; text: string }
  | { kind: "key_press"; key: string }
  | { kind: "scroll"; x: number; y: number; delta_x: number; delta_y: number }
  | { kind: "wait"; duration_ms: number };

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
