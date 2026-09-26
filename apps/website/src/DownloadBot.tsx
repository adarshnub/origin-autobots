import { useEffect, useRef, useState } from "react";
import type { RefObject } from "react";
import { Canvas, useFrame } from "@react-three/fiber";
import { RoundedBox } from "@react-three/drei";
import * as THREE from "three";

type Motion = { x: number; y: number; reduced: boolean };

function Eye({ x }: { x: number }) {
  return <group position={[x, 0, .34]}>
    <mesh rotation={[Math.PI / 2, 0, 0]}><cylinderGeometry args={[.31, .31, .14, 36]} /><meshStandardMaterial color="#e9edfa" metalness={.65} roughness={.22} /></mesh>
    <mesh position={[0, 0, .09]}><sphereGeometry args={[.26, 32, 24]} /><meshPhysicalMaterial color="#152034" metalness={.3} roughness={.14} clearcoat={1} /></mesh>
    <mesh position={[.02, .01, .32]}><sphereGeometry args={[.12, 24, 16]} /><meshStandardMaterial color="#80d7f8" emissive="#69d6fb" emissiveIntensity={.5} /></mesh>
    <mesh position={[.02, .01, .41]}><sphereGeometry args={[.065, 20, 12]} /><meshBasicMaterial color="#14223b" /></mesh>
    <mesh position={[-.03, .075, .43]}><sphereGeometry args={[.035, 12, 10]} /><meshBasicMaterial color="white" /></mesh>
  </group>;
}

function Arm({ side, armRef }: { side: number; armRef: RefObject<THREE.Group | null> }) {
  return <group ref={armRef} position={[side * .67, .08, 0]}>
    <mesh><sphereGeometry args={[.13, 20, 16]} /><meshStandardMaterial color="#77859c" metalness={.55} /></mesh>
    <RoundedBox args={[.2, .42, .22]} radius={.08} smoothness={3} position={[0, -.25, 0]}><meshPhysicalMaterial color="#afa3fa" roughness={.26} clearcoat={.8} /></RoundedBox>
    <mesh position={[0, -.49, 0]}><sphereGeometry args={[.105, 16, 12]} /><meshStandardMaterial color="#8997ae" metalness={.5} /></mesh>
    <RoundedBox args={[.27, .23, .24]} radius={.09} smoothness={3} position={[0, -.62, .02]}><meshStandardMaterial color="#fffdf8" roughness={.32} /></RoundedBox>
    <RoundedBox args={[.075, .16, .1]} radius={.03} smoothness={2} position={[side * .15, -.61, .09]}><meshStandardMaterial color="#f6f5fc" /></RoundedBox>
  </group>;
}

function Character({ motion, cheer }: { motion: RefObject<Motion>; cheer: boolean }) {
  const root = useRef<THREE.Group>(null);
  const head = useRef<THREE.Group>(null);
  const eyes = useRef<THREE.Group>(null);
  const left = useRef<THREE.Group>(null);
  const right = useRef<THREE.Group>(null);
  const smile = useRef<THREE.Mesh>(null);
  const jets = useRef<THREE.Group>(null);
  useFrame((state, delta) => {
    if (!root.current || !head.current || !eyes.current || !left.current || !right.current || !smile.current || !jets.current) return;
    const { x, y, reduced } = motion.current;
    const time = state.clock.elapsedTime;
    root.current.rotation.y = THREE.MathUtils.damp(root.current.rotation.y, reduced ? -.12 : -.12 + x * .27, 4, delta);
    root.current.rotation.x = THREE.MathUtils.damp(root.current.rotation.x, reduced ? 0 : -y * .1, 4, delta);
    root.current.rotation.z = THREE.MathUtils.damp(root.current.rotation.z, cheer ? -.13 : reduced ? -.04 : -.04 + x * .055, 4, delta);
    root.current.position.y = reduced ? -.06 : -.06 + Math.sin(time * 1.7) * .1;
    head.current.rotation.y = THREE.MathUtils.damp(head.current.rotation.y, reduced ? 0 : x * .16, 5, delta);
    eyes.current.scale.y = !reduced && time % 4.6 > 4.37 && time % 4.6 < 4.48 ? .13 : 1;
    left.current.rotation.z = THREE.MathUtils.damp(left.current.rotation.z, cheer ? .85 : -.5, 6, delta);
    right.current.rotation.z = THREE.MathUtils.damp(right.current.rotation.z, cheer ? -.7 + (reduced ? 0 : Math.sin(time * 9) * .3) : .48, 6, delta);
    smile.current.scale.setScalar(THREE.MathUtils.damp(smile.current.scale.x, cheer ? 1 : .001, 9, delta));
    jets.current.scale.y = reduced ? 1 : 1 + Math.sin(time * 12) * .11;
  });
  return <group ref={root} rotation={[0, -.12, -.04]}>
    <RoundedBox args={[1.12, .9, .76]} radius={.22} smoothness={4}><meshPhysicalMaterial color="#aaa0f4" roughness={.25} metalness={.18} clearcoat={1} /></RoundedBox>
    <RoundedBox args={[.79, .48, .06]} radius={.11} smoothness={3} position={[0, -.025, .392]}><meshStandardMaterial color="#fffefa" roughness={.28} /></RoundedBox>
    <RoundedBox args={[.075, .19, .025]} radius={.035} smoothness={3} position={[-.055, .01, .433]}><meshStandardMaterial color="#65598e" /></RoundedBox>
    <RoundedBox args={[.075, .15, .025]} radius={.035} smoothness={3} position={[.055, 0, .433]}><meshStandardMaterial color="#65598e" /></RoundedBox>
    <mesh position={[0, .55, 0]}><cylinderGeometry args={[.12, .15, .25, 24]} /><meshStandardMaterial color="#91a0b6" metalness={.6} roughness={.24} /></mesh>
    <group ref={head} position={[0, .88, 0]}>
      <RoundedBox args={[1.38, .73, .63]} radius={.24} smoothness={4}><meshPhysicalMaterial color="#f8f6ff" roughness={.24} metalness={.15} clearcoat={1} /></RoundedBox>
      <group ref={eyes}><Eye x={-.34} /><Eye x={.34} /></group>
      <RoundedBox args={[.16, .026, .018]} radius={.012} smoothness={2} position={[0, -.24, .327]}><meshBasicMaterial color="#9b91bc" /></RoundedBox>
      <mesh ref={smile} position={[0, -.18, .334]} rotation={[0, 0, Math.PI]} scale={.001}><torusGeometry args={[.18, .021, 8, 24, Math.PI]} /><meshBasicMaterial color="#66588d" /></mesh>
      <mesh position={[.43, .47, -.08]} rotation={[0, 0, -.16]}><cylinderGeometry args={[.025, .03, .3, 12]} /><meshStandardMaterial color="#8995ab" metalness={.6} /></mesh>
      <mesh position={[.45, .63, -.08]}><sphereGeometry args={[.07, 16, 12]} /><meshStandardMaterial color="#b8afff" emissive="#a79df2" emissiveIntensity={.35} /></mesh>
    </group>
    <Arm side={-1} armRef={left} /><Arm side={1} armRef={right} />
    {[-.33, .33].map(x => <group key={x} position={[x, -.54, 0]}><mesh><cylinderGeometry args={[.15, .18, .25, 24]} /><meshStandardMaterial color="#73839f" metalness={.6} /></mesh><mesh position={[0, -.14, 0]} rotation={[Math.PI / 2, 0, 0]}><torusGeometry args={[.13, .035, 10, 24]} /><meshStandardMaterial color="#a7ecfb" emissive="#72dbf8" emissiveIntensity={1} /></mesh></group>)}
    <group ref={jets} position={[0, -.74, 0]}>{[-.33, .33].map(x => <mesh key={x} position={[x, -.2, 0]} rotation={[0, 0, Math.PI]}><coneGeometry args={[.09, .39, 16]} /><meshBasicMaterial color="#a5e7ff" transparent opacity={.58} depthWrite={false} /></mesh>)}</group>
  </group>;
}

export function DownloadBot({ cheer }: { cheer: boolean }) {
  const motion = useRef<Motion>({ x: 0, y: 0, reduced: false });
  const [visible, setVisible] = useState(!document.hidden);
  useEffect(() => {
    const query = matchMedia("(prefers-reduced-motion: reduce)");
    const move = (event: PointerEvent) => {
      motion.current.x = event.clientX / innerWidth * 2 - 1;
      motion.current.y = event.clientY / innerHeight * 2 - 1;
    };
    const reduce = () => { motion.current.reduced = query.matches; };
    const visibility = () => setVisible(!document.hidden);
    reduce();
    addEventListener("pointermove", move, { passive: true });
    query.addEventListener("change", reduce);
    document.addEventListener("visibilitychange", visibility);
    return () => {
      removeEventListener("pointermove", move);
      query.removeEventListener("change", reduce);
      document.removeEventListener("visibilitychange", visibility);
    };
  }, []);
  return <div className="download-bot-hero">
    <div className="download-bot-canvas" aria-hidden="true"><Canvas frameloop={visible ? "always" : "never"} dpr={[1, 1.6]} camera={{ position: [0, .15, 4.4], fov: 40 }} gl={{ alpha: true, antialias: true }} fallback={<div className="download-bot-fallback">◉ ◉</div>}>
      <ambientLight intensity={1.25} /><directionalLight position={[3, 5, 6]} intensity={3} color="#fff4e4" /><directionalLight position={[-4, 1, 3]} intensity={1.7} color="#cbd7ff" />
      <Character motion={motion} cheer={cheer} />
    </Canvas></div>
    <div className={`download-bot-bubble${cheer ? " is-cheering" : ""}`} role="status" aria-live="polite">{cheer ? "Autobots, roll out!" : "Download Autobots to get started ↓"}</div>
  </div>;
}
