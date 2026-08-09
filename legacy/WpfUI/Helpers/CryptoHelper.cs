using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;

namespace RcktMon.Helpers
{
    public class CryptoHelper
    {
        public static string GetMachineGuid()
        {
            string location = @"SOFTWARE\Microsoft\Cryptography";
            string name = "MachineGuid";

            using (RegistryKey localMachineX64View =
                RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
            {
                using (RegistryKey rk = localMachineX64View.OpenSubKey(location))
                {
                    if (rk == null)
                        throw new KeyNotFoundException(
                            string.Format("Key Not Found: {0}", location));

                    object machineGuid = rk.GetValue(name);
                    if (machineGuid == null)
                        throw new IndexOutOfRangeException(
                            string.Format("Index Not Found: {0}", name));

                    return machineGuid.ToString();
                }
            }
        }

        public static string Encrypt(string value)
        {
            var guid = GetMachineGuid();
            return AESEncryption.Encrypt(value, guid);
        }

        public static string Decrypt(string value)
        {
            var guid = GetMachineGuid();
            return AESEncryption.Decrypt(value, guid);
        }

        /// <summary>
      /// Utility class that handles encryption
      /// </summary>
      private static class AESEncryption
      {
          #region Static Functions
   
          /// <summary>
          /// Encrypts a string
          /// </summary>
          /// <param name="plainText">Text to be encrypted</param>
          /// <param name="password">Password to encrypt with</param>
          /// <param name="salt">Salt to encrypt with</param>
          /// <param name="hashAlgo">Can be either SHA1 or MD5</param>
          /// <param name="passwordIteration">Number of iterations to do</param>
          /// <param name="initialVector">Needs to be 16 ASCII characters long</param>
          /// <param name="KeySize">Can be 128, 192, or 256</param>
          /// <returns>An encrypted string</returns>
          public static string Encrypt(string plainText, string password,
              string salt = "SomeSalt", string hashAlgo = "SHA1",
              int passwordIteration = 2, string initialVector = "OFRna73m*aze01xY",
              int KeySize = 256)
          {
              if (string.IsNullOrEmpty(plainText))
                  return "";
              byte[] InitialVectorBytes = Encoding.ASCII.GetBytes(initialVector);
              byte[] SaltValueBytes = Encoding.ASCII.GetBytes(salt);
              byte[] PlainTextBytes = Encoding.UTF8.GetBytes(plainText);
              PasswordDeriveBytes DerivedPassword = new PasswordDeriveBytes(password, SaltValueBytes, hashAlgo, passwordIteration);
              byte[] KeyBytes = DerivedPassword.GetBytes(KeySize / 8);
              var SymmetricKey = Aes.Create();
              SymmetricKey.Mode = CipherMode.CBC;
              byte[] CipherTextBytes = null;
              using (ICryptoTransform Encryptor = SymmetricKey.CreateEncryptor(KeyBytes, InitialVectorBytes))
              {
                  using (MemoryStream MemStream = new MemoryStream())
                  {
                      using (CryptoStream CryptoStream = new CryptoStream(MemStream, Encryptor, CryptoStreamMode.Write))
                      {
                          CryptoStream.Write(PlainTextBytes, 0, PlainTextBytes.Length);
                          CryptoStream.FlushFinalBlock();
                          CipherTextBytes = MemStream.ToArray();
                          MemStream.Close();
                          CryptoStream.Close();
                      }
                  }
              }
              SymmetricKey.Clear();
              return Convert.ToBase64String(CipherTextBytes);
          }
   
          /// <summary>
          /// Decrypts a string
          /// </summary>
          /// <param name="cipherText">Text to be decrypted</param>
          /// <param name="password">Password to decrypt with</param>
          /// <param name="salt">Salt to decrypt with</param>
          /// <param name="hashAlog">Can be either SHA1 or MD5</param>
          /// <param name="passwordIteration">Number of iterations to do</param>
          /// <param name="initialVector">Needs to be 16 ASCII characters long</param>
          /// <param name="KeySize">Can be 128, 192, or 256</param>
          /// <returns>A decrypted string</returns>
          public static string Decrypt(string cipherText, string password,
              string salt = "SomeSalt", string hashAlgo = "SHA1",
              int passwordIteration = 2, string initialVector = "OFRna73m*aze01xY",
              int KeySize = 256)
          {
              if (string.IsNullOrEmpty(cipherText))
                  return "";
              byte[] InitialVectorBytes = Encoding.ASCII.GetBytes(initialVector);
              byte[] SaltValueBytes = Encoding.ASCII.GetBytes(salt);
              byte[] CipherTextBytes = Convert.FromBase64String(cipherText);
              PasswordDeriveBytes DerivedPassword = new PasswordDeriveBytes(password, SaltValueBytes, hashAlgo, passwordIteration);
              byte[] KeyBytes = DerivedPassword.GetBytes(KeySize / 8);
              var SymmetricKey = Aes.Create();
              SymmetricKey.Mode = CipherMode.CBC;
              byte[] PlainTextBytes = new byte[CipherTextBytes.Length];
              int bytesRead = 0;
              using (ICryptoTransform Decryptor = SymmetricKey.CreateDecryptor(KeyBytes, InitialVectorBytes))
              {
                  using (MemoryStream MemStream = new MemoryStream(CipherTextBytes))
                  {
                      using (CryptoStream CryptoStream = new CryptoStream(MemStream, Decryptor, CryptoStreamMode.Read))
                      {
                            int count = 0;
                            do
                            {
                                count = CryptoStream.Read(PlainTextBytes, 0 + bytesRead, PlainTextBytes.Length - bytesRead);
                                bytesRead += count;
                            }
                            while (count > 0);
                          MemStream.Close();
                          CryptoStream.Close();
                      }
                  }
              }
              SymmetricKey.Clear();
              return Encoding.UTF8.GetString(PlainTextBytes, 0, bytesRead);
          }
   
          #endregion
      }
    
    }
}
