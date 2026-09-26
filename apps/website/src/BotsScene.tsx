import { useEffect, useRef, useState } from "react";
import type { RefObject } from "react";
import { Canvas, useFrame, useThree } from "@react-three/fiber";
import { Environment, Lightformer, RoundedBox } from "@react-three/drei";
import * as THREE from "three";

export type FlightMotion = { phase: number; x: number; y: number; reduced: boolean };

export function useFlight() {
  const motion = useRef<FlightMotion>({ phase: 0, x: 0, y: 0, reduced: false });
  useEffect(() => {
    const query = matchMedia("(prefers-reduced-motion: reduce)");
    const update = () => {
      const sections = Array.from(document.querySelectorAll<HTMLElement>("[data-flight-section]"));
      let phase = 0;
      for (let i = 0; i < sections.length - 1; i++) {
        const start = sections[i].offsetTop;
        const end = sections[i + 1].offsetTop;
        if (scrollY >= start) phase = i + Math.min(1, (scrollY - start) / Math.max(1, end - start));
      }
      motion.current.phase = phase;
    };
    const pointer = (event: PointerEvent) => {
      motion.current.x = event.clientX / innerWidth * 2 - 1;
      motion.current.y = event.clientY / innerHeight * 2 - 1;
    };
    const reduced = () => { motion.current.reduced = query.matches; };
    update(); reduced();
    addEventListener("scroll", update, { passive: true });
    addEventListener("resize", update);
    addEventListener("pointermove", pointer, { passive: true });
    query.addEventListener("change", reduced);
    return () => {
      removeEventListener("scroll", update); removeEventListener("resize", update);
      removeEventListener("pointermove", pointer); query.removeEventListener("change", reduced);
    };
  }, []);
  return motion;
}

type Stop = [number, number, number, number];
const routes: Stop[][] = [
  [[.35,.03,1.02,-.2],[-.36,.03,.82,.35],[.39,-.08,.72,-.38],[-.37,-.02,.78,.25],[.35,.06,.9,-.3],[.36,.05,.88,-.28]],
  [[-.37,-.11,.68,.28],[.37,-.26,.56,-.45],[-.38,.26,.54,.4],[.36,.23,.58,-.3],[-.34,-.04,.67,.2],[-.37,-.25,.6,.3]],
  [[-.31,.29,.43,.2],[.32,.32,.47,-.25],[-.36,-.31,.44,.2],[.34,-.32,.42,-.3],[.04,.3,.4,-.2],[.30,-.33,.45,-.2]],
];

function Eye({ x, tint }: { x: number; tint: string }) {
  return <group position={[x, 0, .36]}>
    <mesh rotation={[Math.PI / 2, 0, 0]}><cylinderGeometry args={[.31,.32,.16,40]} /><meshStandardMaterial color="#dce1ed" metalness={.7} roughness={.22} /></mesh>
    <mesh position={[0,0,.09]}><sphereGeometry args={[.263,32,24]} /><meshPhysicalMaterial color="#152033" metalness={.32} roughness={.14} clearcoat={1} /></mesh>
    <mesh position={[.018,.009,.315]}><sphereGeometry args={[.125,24,16]} /><meshStandardMaterial color={tint} emissive={tint} emissiveIntensity={.35} roughness={.25} /></mesh>
    <mesh position={[.018,.009,.414]}><sphereGeometry args={[.066,20,12]} /><meshBasicMaterial color="#111b30" /></mesh>
    <mesh position={[-.026,.07,.429]}><sphereGeometry args={[.034,14,10]} /><meshBasicMaterial color="#ffffff" /></mesh>
    <mesh position={[.075,-.045,.421]}><sphereGeometry args={[.014,10,8]} /><meshBasicMaterial color="#bddbff" /></mesh>
  </group>;
}

function Arm({ side, color, armRef }: { side: number; color: string; armRef: RefObject<THREE.Group | null> }) {
  return <group ref={armRef} position={[side * .62,.09,0]} rotation={[.05,0,side * .22]}>
    <mesh><sphereGeometry args={[.13,20,16]} /><meshStandardMaterial color="#7d879b" metalness={.6} roughness={.28} /></mesh>
    <RoundedBox args={[.19,.38,.21]} radius={.07} smoothness={3} position={[0,-.23,0]}><meshStandardMaterial color={color} roughness={.28} /></RoundedBox>
    <mesh position={[0,-.45,0]}><sphereGeometry args={[.105,16,12]} /><meshStandardMaterial color="#8c96ab" metalness={.65} roughness={.25} /></mesh>
    <RoundedBox args={[.23,.24,.21]} radius={.065} smoothness={3} position={[0,-.6,.02]}><meshStandardMaterial color="#f4f5f9" roughness={.3} /></RoundedBox>
    <RoundedBox args={[.07,.14,.1]} radius={.025} smoothness={2} position={[side * .125,-.61,.09]}><meshStandardMaterial color="#e5e8ee" roughness={.35} /></RoundedBox>
  </group>;
}

function TaskTile({ tint }: { tint: string }) {
  return <group position={[.73,-.5,.47]} rotation={[.04,-.1,-.1]}>
    <RoundedBox args={[.54,.64,.08]} radius={.09} smoothness={3}><meshStandardMaterial color="#fffdf8" roughness={.34} /></RoundedBox>
    <mesh position={[-.12,.16,.05]}><circleGeometry args={[.064,20]} /><meshBasicMaterial color={tint} /></mesh>
    {[0,-.10,-.20].map((y,i) => <RoundedBox key={i} args={[i === 2 ? .2 : .33,.025,.02]} radius={.009} smoothness={2} position={[i === 2 ? -.065 : 0,y,.052]}><meshBasicMaterial color={i === 2 ? tint : "#cbd0df"} /></RoundedBox>)}
  </group>;
}

function Bot({ motion, index, color, eyeColor }: { motion: RefObject<FlightMotion>; index: number; color: string; eyeColor: string }) {
  const root = useRef<THREE.Group>(null);
  const head = useRef<THREE.Group>(null);
  const eyes = useRef<THREE.Group>(null);
  const left = useRef<THREE.Group>(null);
  const right = useRef<THREE.Group>(null);
  const jets = useRef<THREE.Group>(null);
  const { viewport } = useThree();
  useFrame((state, delta) => {
    if (!root.current || !head.current || !eyes.current || !left.current || !right.current || !jets.current) return;
    const { phase, x, y, reduced } = motion.current;
    const path = routes[index];
    const part = Math.min(path.length - 2, Math.floor(phase));
    const raw = THREE.MathUtils.clamp(phase - part, 0, 1);
    const t = raw * raw * (3 - 2 * raw);
    const from = path[part], to = path[part + 1];
    const time = reduced ? 0 : state.clock.elapsedTime;
    const ease = reduced ? 1 : 1 - Math.exp(-Math.min(delta,.05) * 5);
    const mobile = viewport.width < 6;
    const travel = Math.sin(raw * Math.PI);
    const px = THREE.MathUtils.lerp(from[0],to[0],t) * viewport.width;
    const py = THREE.MathUtils.lerp(from[1],to[1],t) * viewport.height;
    const bob = Math.sin(time * 1.6 + index * 1.8) * .065;
    root.current.visible = !mobile || index < 2;
    root.current.position.x = THREE.MathUtils.lerp(root.current.position.x, px + x * .045, ease);
    root.current.position.y = THREE.MathUtils.lerp(root.current.position.y, py + bob + (mobile ? -.6 : 0), ease);
    root.current.position.z = -.1 + travel * .65 + index * -.18;
    const scale = THREE.MathUtils.lerp(from[2],to[2],t) * (mobile ? .58 : 1);
    root.current.scale.setScalar(scale);
    root.current.rotation.z = THREE.MathUtils.lerp(root.current.rotation.z, THREE.MathUtils.lerp(from[3],to[3],t) + Math.sin(time * .8 + index) * .05 - travel * .18, ease);
    root.current.rotation.y = THREE.MathUtils.lerp(root.current.rotation.y, index === 1 ? .35 - x * .08 : -.25 + x * .13, ease);
    head.current.rotation.y = x * .21;
    head.current.rotation.x = y * .13 + Math.sin(time + index) * .035;
    head.current.rotation.z = Math.sin(time * .65 + index) * .04;
    const blink = time % (4.2 + index * .7);
    eyes.current.scale.y = blink > 3.95 && blink < 4.1 ? .13 : 1;
    left.current.rotation.z = -.36 - Math.sin(time * 1.3 + index) * .17;
    right.current.rotation.z = .55 + Math.sin(time * 1.4 + index) * .17;
    jets.current.scale.y = 1 + Math.sin(time * 13) * .12 + travel * .5;
  });
  return <group ref={root}>
    <RoundedBox args={[1.05,.86,.71]} radius={.20} smoothness={4}><meshPhysicalMaterial color={color} metalness={.16} roughness={.28} clearcoat={.8} /></RoundedBox>
    <RoundedBox args={[.75,.45,.055]} radius={.1} smoothness={3} position={[0,-.02,.362]}><meshStandardMaterial color="#f6f6f9" roughness={.3} /></RoundedBox>
    <mesh position={[0,.02,.402]}><circleGeometry args={[.1,24]} /><meshStandardMaterial color={eyeColor} emissive={eyeColor} emissiveIntensity={.25} /></mesh>
    <RoundedBox args={[.19,.03,.01]} radius={.01} smoothness={2} position={[0,-.16,.4]}><meshBasicMaterial color="#adb4c7" /></RoundedBox>
    <RoundedBox args={[.56,.56,.25]} radius={.1} smoothness={3} position={[0,.05,-.42]}><meshStandardMaterial color="#64718c" metalness={.5} roughness={.3} /></RoundedBox>
    <mesh position={[0,.53,0]}><cylinderGeometry args={[.11,.15,.25,24]} /><meshStandardMaterial color="#8995aa" metalness={.7} roughness={.22} /></mesh>
    <group ref={head} position={[0,.85,0]}>
      <RoundedBox args={[1.30,.67,.59]} radius={.22} smoothness={4}><meshPhysicalMaterial color={index === 0 ? "#fff5d7" : "#f1f1ff"} roughness={.28} metalness={.18} clearcoat={1} /></RoundedBox>
      <group ref={eyes}><Eye x={-.325} tint={eyeColor} /><Eye x={.325} tint={eyeColor} /></group>
      <mesh position={[.41,.45,-.08]} rotation={[0,0,-.16]}><cylinderGeometry args={[.022,.03,.31,12]} /><meshStandardMaterial color="#8790a6" metalness={.65} roughness={.25} /></mesh>
      <mesh position={[.435,.61,-.08]}><sphereGeometry args={[.067,16,12]} /><meshStandardMaterial color={color} emissive={color} emissiveIntensity={.3} /></mesh>
    </group>
    <Arm side={-1} color={color} armRef={left} /><Arm side={1} color={color} armRef={right} />
    {[-.31,.31].map((x) => <group key={x} position={[x,-.5,-.02]}>
      <mesh><cylinderGeometry args={[.14,.17,.23,24]} /><meshStandardMaterial color="#6f7c97" metalness={.68} roughness={.2} /></mesh>
      <mesh position={[0,-.12,0]} rotation={[Math.PI/2,0,0]}><torusGeometry args={[.12,.032,10,24]} /><meshStandardMaterial color="#a1e5ff" emissive="#72d9ff" emissiveIntensity={1.2} /></mesh>
    </group>)}
    <group ref={jets} position={[0,-.65,-.02]}>{[-.31,.31].map((x) => <mesh key={x} position={[x,-.2,0]} rotation={[0,0,Math.PI]}><coneGeometry args={[.088,.42,16]} /><meshBasicMaterial color="#9ddfff" transparent opacity={.58} depthWrite={false} /></mesh>)}</group>
    <TaskTile tint={index === 0 ? "#6b62ed" : index === 1 ? "#efb140" : "#4ca9dc"} />
  </group>;
}

export function BotsScene({ motion }: { motion: RefObject<FlightMotion> }) {
  const [visible, setVisible] = useState(!document.hidden);
  useEffect(() => {
    const change = () => setVisible(!document.hidden);
    document.addEventListener("visibilitychange",change);
    return () => document.removeEventListener("visibilitychange",change);
  }, []);
  return <div className="bot-canvas" aria-hidden="true"><Canvas frameloop={visible ? "always" : "never"} dpr={[1,1.6]} camera={{ position: [0,0,10], fov: 40 }} gl={{ alpha: true, antialias: true }} fallback={<div className="bot-fallback">✦</div>}>
    <ambientLight intensity={1.2} /><directionalLight position={[3,6,6]} intensity={3.2} color="#ffefdb" /><directionalLight position={[-4,1,3]} intensity={1.5} color="#c3d6ff" />
    <Environment resolution={128}><Lightformer intensity={3} position={[0,5,-2]} scale={[10,5,1]} /><Lightformer intensity={2} position={[-5,2,2]} rotation={[0,Math.PI/2,0]} scale={[5,8,1]} /><Lightformer intensity={2} position={[5,1,0]} rotation={[0,-Math.PI/2,0]} scale={[3,7,1]} /></Environment>
    <Bot motion={motion} index={0} color="#ffc65b" eyeColor="#74cced" /><Bot motion={motion} index={1} color="#aca4f6" eyeColor="#9cdfe9" /><Bot motion={motion} index={2} color="#8acded" eyeColor="#b1a5ff" />
  </Canvas></div>;
}
