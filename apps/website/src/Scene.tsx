import { useEffect, useMemo, useRef } from "react";
import type { RefObject } from "react";
import { Canvas, useFrame } from "@react-three/fiber";
import * as THREE from "three";

export type Motion = { scroll: number; pointerX: number; pointerY: number; reduced: boolean };

export function useMotion() {
  const motion = useRef<Motion>({ scroll: 0, pointerX: 0, pointerY: 0, reduced: false });
  useEffect(() => {
    const reducedQuery = matchMedia("(prefers-reduced-motion: reduce)");
    const onScroll = () => {
      const distance = Math.max(1, document.documentElement.scrollHeight - innerHeight);
      motion.current.scroll = Math.min(1, scrollY / distance);
      document.documentElement.style.setProperty("--page-progress", `${motion.current.scroll * 100}%`);
    };
    const onPointer = (event: PointerEvent) => {
      motion.current.pointerX = event.clientX / innerWidth * 2 - 1;
      motion.current.pointerY = event.clientY / innerHeight * 2 - 1;
    };
    const onReduced = () => { motion.current.reduced = reducedQuery.matches; };
    onReduced(); onScroll();
    addEventListener("scroll", onScroll, { passive: true });
    addEventListener("pointermove", onPointer, { passive: true });
    addEventListener("resize", onScroll);
    reducedQuery.addEventListener("change", onReduced);
    return () => {
      removeEventListener("scroll", onScroll); removeEventListener("pointermove", onPointer);
      removeEventListener("resize", onScroll); reducedQuery.removeEventListener("change", onReduced);
    };
  }, []);
  return motion;
}

const stops = [
  { at: 0, x: 2.55, y: 0, z: 0, scale: 1.1 },
  { at: 0.23, x: -2.75, y: 0.1, z: -0.4, scale: 0.82 },
  { at: 0.46, x: 2.8, y: 0, z: -0.7, scale: 0.76 },
  { at: 0.71, x: -2.65, y: 0, z: -0.6, scale: 0.76 },
  { at: 1, x: 2.15, y: 0.1, z: -0.2, scale: 0.96 },
];

function positionAt(progress: number) {
  let index = stops.findIndex((stop) => stop.at >= progress);
  if (index < 1) index = 1;
  const from = stops[index - 1], to = stops[index];
  const raw = THREE.MathUtils.clamp((progress - from.at) / (to.at - from.at), 0, 1);
  const t = raw * raw * (3 - 2 * raw);
  return { x: THREE.MathUtils.lerp(from.x, to.x, t), y: THREE.MathUtils.lerp(from.y, to.y, t), z: THREE.MathUtils.lerp(from.z, to.z, t), scale: THREE.MathUtils.lerp(from.scale, to.scale, t) };
}

function Core({ motion }: { motion: RefObject<Motion> }) {
  const body = useRef<THREE.Group>(null);
  const center = useRef<THREE.Group>(null);
  const rings = useRef<THREE.Group>(null);
  const orbiters = useRef<THREE.Group>(null);
  useFrame((state, delta) => {
    if (!body.current || !center.current || !rings.current || !orbiters.current) return;
    const { scroll, pointerX, pointerY, reduced } = motion.current;
    const target = positionAt(scroll);
    const d = Math.min(delta, 0.05), ease = 1 - Math.exp(-d * 3.2);
    body.current.position.x = THREE.MathUtils.lerp(body.current.position.x, target.x + pointerX * 0.2, ease);
    body.current.position.y = THREE.MathUtils.lerp(body.current.position.y, target.y - pointerY * 0.16, ease);
    body.current.position.z = THREE.MathUtils.lerp(body.current.position.z, target.z, ease);
    body.current.scale.setScalar(THREE.MathUtils.lerp(body.current.scale.x, target.scale, ease));
    body.current.rotation.y = THREE.MathUtils.lerp(body.current.rotation.y, scroll * Math.PI * 2.7 + pointerX * 0.16, ease);
    body.current.rotation.x = THREE.MathUtils.lerp(body.current.rotation.x, scroll * 0.6 - pointerY * 0.15, ease);
    if (!reduced) {
      center.current.rotation.y += d * 0.22;
      center.current.rotation.z += d * 0.1;
      rings.current.rotation.z -= d * 0.075;
      orbiters.current.rotation.y -= d * 0.12;
      body.current.position.y += Math.sin(state.clock.elapsedTime * 0.8) * 0.035;
    }
  });
  return <group ref={body} position={[2.55, 0, 0]}>
    <group ref={rings}>
      <mesh rotation={[0.28, 0.3, 0.2]}><torusGeometry args={[1.8, 0.013, 8, 160]} /><meshBasicMaterial color="#d8ff65" transparent opacity={0.7} /></mesh>
      <mesh rotation={[1.2, 0.2, 0.65]}><torusGeometry args={[1.55, 0.018, 8, 160]} /><meshBasicMaterial color="#b4c9b0" transparent opacity={0.43} /></mesh>
      <mesh rotation={[-0.43, 1.08, -0.3]}><torusGeometry args={[1.91, 0.009, 8, 160]} /><meshBasicMaterial color="#d8ff65" transparent opacity={0.31} /></mesh>
      <mesh rotation={[0.2, 0.4, 0]}><icosahedronGeometry args={[1.45, 1]} /><meshBasicMaterial color="#8db697" wireframe transparent opacity={0.1} /></mesh>
    </group>
    <group ref={center}>
      <mesh><icosahedronGeometry args={[0.94, 1]} /><meshPhysicalMaterial color="#bacdb6" metalness={0.82} roughness={0.25} clearcoat={0.8} flatShading /></mesh>
      <mesh scale={1.014}><icosahedronGeometry args={[0.94, 1]} /><meshBasicMaterial color="#e2ffd0" wireframe transparent opacity={0.28} /></mesh>
      <mesh scale={0.5}><icosahedronGeometry args={[0.94, 1]} /><meshBasicMaterial color="#d8ff65" /></mesh>
    </group>
    <group ref={orbiters}>{([[1.9, 0.8, 0.3], [-1.65, -0.9, 0.6], [0.3, 1.8, -0.3], [-0.5, -1.85, -0.2]] as [number, number, number][]).map((place, i) =>
      <mesh key={i} position={place} rotation={[i, i * 0.6, i * 0.4]}><octahedronGeometry args={[i === 0 ? 0.16 : 0.095, 0]} /><meshStandardMaterial color={i === 0 ? "#d8ff65" : "#d5ded1"} metalness={0.6} roughness={0.25} /></mesh>)}</group>
  </group>;
}

function Field({ motion }: { motion: RefObject<Motion> }) {
  const group = useRef<THREE.Group>(null);
  const dots = useMemo(() => Array.from({ length: 38 }, (_, i) => {
    const a = i * 2.39996, radius = 2.8 + i % 8 * 0.65;
    return [Math.cos(a) * radius, Math.sin(a * 1.3) * 2.75, -2 - i % 5 * 0.9] as [number, number, number];
  }), []);
  useFrame((_, delta) => { if (group.current && !motion.current.reduced) group.current.rotation.z += Math.min(delta, 0.05) * 0.006; });
  return <group ref={group}>{dots.map((place, i) => <mesh key={i} position={place}><sphereGeometry args={[i % 8 === 0 ? 0.018 : 0.009, 5, 5]} /><meshBasicMaterial color={i % 8 === 0 ? "#d8ff65" : "#87978d"} transparent opacity={i % 8 === 0 ? 0.8 : 0.4} /></mesh>)}</group>;
}

export function Scene({ motion }: { motion: RefObject<Motion> }) {
  return <div className="scene" aria-hidden="true"><Canvas dpr={[1, 1.7]} camera={{ position: [0, 0, 9], fov: 45 }} gl={{ alpha: true, antialias: true, powerPreference: "high-performance" }} fallback={<div className="scene-fallback" />}>
    <ambientLight intensity={1.45} color="#d6e9d5" /><directionalLight position={[4, 5, 8]} intensity={2.6} color="#eeffdb" /><pointLight position={[-4, -2, 4]} intensity={25} color="#c4ff55" distance={13} />
    <Field motion={motion} /><Core motion={motion} />
  </Canvas></div>;
}
