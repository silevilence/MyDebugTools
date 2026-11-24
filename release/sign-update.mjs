import { createPrivateKey, createPublicKey, sign } from 'node:crypto'
import { readFileSync, writeFileSync } from 'node:fs'
import { join } from 'node:path'

// The secret is a PKCS#8 Ed25519 PEM. Only the raw public key enters MSBuild.
const key = createPrivateKey(process.env.UPDATE_SIGNING_KEY)
if (key.asymmetricKeyType !== 'ed25519') throw Error('UPDATE_SIGNING_KEY 需要 Ed25519 私钥')
const publicKey = Buffer.from(createPublicKey(key).export({ format: 'jwk' }).x, 'base64url').toString('base64')
if (process.argv[2] === 'public') {
  console.log(publicKey)
} else {
  const [folder, tag, releaseRoot, notesFile] = process.argv.slice(2)
  if (!/^[Vv]\d+\.\d+\.\d+$/.test(tag) || publicKey !== process.env.UpdatePublicKey) {
    throw Error('发布标签或用于构建宿主的公钥不匹配')
  }
  const version = tag.slice(1)
  const name = `MyDebugTools-${version}-win-x64.exe`
  const installer = readFileSync(join(folder, name))
  const signature = bytes => sign(null, bytes, key).toString('base64')
  const escape = text => text.replaceAll('&', '&amp;').replaceAll('<', '&lt;').replaceAll('>', '&gt;').replaceAll('"', '&quot;')
  const root = escape(releaseRoot)
  const notes = escape(readFileSync(notesFile, 'utf8'))
  const xml = Buffer.from(`<?xml version="1.0" encoding="utf-8"?>
<rss version="2.0" xmlns:sparkle="http://www.andymatuschak.org/xml-namespaces/sparkle">
  <channel><title>MyDebugTools</title><item>
    <title>MyDebugTools ${version}</title>
    <description>${notes}</description>
    <sparkle:releaseNotesLink>${root}/tag/${tag}</sparkle:releaseNotesLink>
    <enclosure url="${root}/download/${tag}/${name}" length="${installer.length}" type="application/octet-stream"
      sparkle:version="${version}" sparkle:os="windows" sparkle:edSignature="${signature(installer)}"/>
  </item></channel>
</rss>
`)
  writeFileSync(join(folder, 'appcast.xml'), xml)
  writeFileSync(join(folder, 'appcast.xml.signature'), signature(xml))
}
