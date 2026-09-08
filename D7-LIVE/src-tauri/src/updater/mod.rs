use base64::Engine;
use ed25519_dalek::{Signature,Verifier,VerifyingKey};
use semver::Version;
use serde::Deserialize;
use sha2::{Digest,Sha256};
use thiserror::Error;

#[derive(Debug,Deserialize)] pub struct Manifest {pub version:String,pub notes:String,pub published_at:String,pub windows:WindowsPackage}
#[derive(Debug,Deserialize)] pub struct WindowsPackage {pub url:String,pub sha256:String,pub signature:String}
#[derive(Debug,Error)] pub enum UpdateError {#[error("update endpoint is not configured")] NotConfigured,#[error("invalid manifest: {0}")] Invalid(String)}

pub fn sha256_hex(bytes:&[u8])->String{format!("{:x}",Sha256::digest(bytes))}
pub fn is_newer(current:&str,candidate:&str)->Result<bool,semver::Error>{Ok(Version::parse(candidate)?>Version::parse(current)?)}
pub fn verify_signature(bytes:&[u8],signature_b64:&str,public_key:[u8;32])->Result<(),UpdateError>{let sig_bytes=base64::engine::general_purpose::STANDARD.decode(signature_b64).map_err(|e|UpdateError::Invalid(e.to_string()))?;let signature=Signature::from_slice(&sig_bytes).map_err(|e|UpdateError::Invalid(e.to_string()))?;let key=VerifyingKey::from_bytes(&public_key).map_err(|e|UpdateError::Invalid(e.to_string()))?;key.verify(bytes,&signature).map_err(|e|UpdateError::Invalid(e.to_string()))}
pub async fn human_check_message()->Result<String,UpdateError>{Err(UpdateError::NotConfigured)}

#[cfg(test)] mod tests{use super::*;#[test] fn semver_check(){assert!(is_newer("1.0.0","1.0.1").unwrap());}#[test] fn hash_known(){assert_eq!(sha256_hex(b"abc"),"ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad");}}
