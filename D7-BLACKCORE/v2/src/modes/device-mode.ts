export interface BasicDeviceSnapshot{platform:string;release:string;arch:string;hostname:string;cpuModel:string;logicalCpus:number;totalMemoryBytes:number;freeMemoryBytes:number;uptimeSeconds:number;}
export interface DeviceProbeAdapter{snapshot():BasicDeviceSnapshot;}
export class DeviceMode{constructor(private readonly probe:DeviceProbeAdapter){}snapshot():BasicDeviceSnapshot{const value=this.probe.snapshot();if(value.logicalCpus<1)throw new Error("INVALID_DEVICE_SNAPSHOT");return structuredClone(value);}}
