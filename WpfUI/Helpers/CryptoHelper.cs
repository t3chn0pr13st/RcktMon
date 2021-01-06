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
          /// <param name="PlainText">Text to be encrypted</param>
          /// <param name="Password">Password to encrypt with</param>
          /// <param name="Salt">Salt to encrypt with</param>
          /// <param name="HashAlgorithm">Can be either SHA1 or MD5</param>
          /// <param name="PasswordIterations">Number of iterations to do</param>
          /// <param name="InitialVector">Needs to be 16 ASCII characters long</param>
          /// <param name="KeySize">Can be 128, 192, or 256</param>
          /// <returns>An encrypted string</returns>
          public static string Encrypt(string PlainText, string Password,
              string Salt = "SomeSalt", string HashAlgorithm = "SHA1",
              int PasswordIterations = 2, string InitialVector = "OFRna73m*aze01xY",
              int KeySize = 256)
          {
              if (string.IsNullOrEmpty(PlainText))
                  return "";
              byte[] InitialVectorBytes = Encoding.ASCII.GetBytes(InitialVector);
              byte[] SaltValueBytes = Encoding.ASCII.GetBytes(Salt);
              byte[] PlainTextBytes = Encoding.UTF8.GetBytes(PlainText);
              PasswordDeriveBytes DerivedPassword = new PasswordDeriveBytes(Password, SaltValueBytes, HashAlgorithm, PasswordIterations);
              byte[] KeyBytes = DerivedPassword.GetBytes(KeySize / 8);
              RijndaelManaged SymmetricKey = new RijndaelManaged();
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
          /// <param name="CipherText">Text to be decrypted</param>
          /// <param name="Password">Password to decrypt with</param>
          /// <param name="Salt">Salt to decrypt with</param>
          /// <param name="HashAlgorithm">Can be either SHA1 or MD5</param>
          /// <param name="PasswordIterations">Number of iterations to do</param>
          /// <param name="InitialVector">Needs to be 16 ASCII characters long</param>
          /// <param name="KeySize">Can be 128, 192, or 256</param>
          /// <returns>A decrypted string</returns>
          public static string Decrypt(string CipherText, string Password,
              string Salt = "SomeSalt", string HashAlgorithm = "SHA1",
              int PasswordIterations = 2, string InitialVector = "OFRna73m*aze01xY",
              int KeySize = 256)
          {
              if (string.IsNullOrEmpty(CipherText))
                  return "";
              byte[] InitialVectorBytes = Encoding.ASCII.GetBytes(InitialVector);
              byte[] SaltValueBytes = Encoding.ASCII.GetBytes(Salt);
              byte[] CipherTextBytes = Convert.FromBase64String(CipherText);
              PasswordDeriveBytes DerivedPassword = new PasswordDeriveBytes(Password, SaltValueBytes, HashAlgorithm, PasswordIterations);
              byte[] KeyBytes = DerivedPassword.GetBytes(KeySize / 8);
              RijndaelManaged SymmetricKey = new RijndaelManaged();
              SymmetricKey.Mode = CipherMode.CBC;
              byte[] PlainTextBytes = new byte[CipherTextBytes.Length];
              int ByteCount = 0;
              using (ICryptoTransform Decryptor = SymmetricKey.CreateDecryptor(KeyBytes, InitialVectorBytes))
              {
                  using (MemoryStream MemStream = new MemoryStream(CipherTextBytes))
                  {
                      using (CryptoStream CryptoStream = new CryptoStream(MemStream, Decryptor, CryptoStreamMode.Read))
                      {
   
                          ByteCount = CryptoStream.Read(PlainTextBytes, 0, PlainTextBytes.Length);
                          MemStream.Close();
                          CryptoStream.Close();
                      }
                  }
              }
              SymmetricKey.Clear();
              return Encoding.UTF8.GetString(PlainTextBytes, 0, ByteCount);
          }
   
          #endregion
      }
    
    }
}
