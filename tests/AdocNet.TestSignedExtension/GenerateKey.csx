using System;
using System.Security.Cryptography;
using System.IO;
var rsa = RSA.Create(2048);
var blob = rsa.ExportRSAPrivateKey();
// SNK format: just the raw RSA key pair blob in CAPI format
File.WriteAllBytes("test.snk", rsa.ExportCspBlob(true));
Console.WriteLine("Key generated.");
