use std::{fs,path::Path};

pub fn atomic_write(path:&Path,data:&[u8])->std::io::Result<()> {
    let tmp=path.with_extension("tmp");
    if let Some(parent)=path.parent(){fs::create_dir_all(parent)?;}
    fs::write(&tmp,data)?;
    if path.exists(){ let backup=path.with_extension("bak"); let _=fs::copy(path,backup); }
    fs::rename(tmp,path)
}
