﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

#if NETCOREAPP2_0

using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Xunit;

namespace Microsoft.AspNetCore.Certificates.Generation.Tests
{
    public class CertificateManagerTests
    {
        public const string TestCertificateSubject = "CN=aspnet.test";

        [Fact]
        public void EnsureCreateHttpsCertificate_CreatesACertificate_WhenThereAreNoHttpsCertificates()
        {
            // Arrange
            const string CertificateName = nameof(EnsureCreateHttpsCertificate_CreatesACertificate_WhenThereAreNoHttpsCertificates) + ".cer";
            var manager = new CertificateManager();

            manager.RemoveAllCertificates(CertificatePurpose.HTTPS, StoreName.My, StoreLocation.CurrentUser, TestCertificateSubject);
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                manager.RemoveAllCertificates(CertificatePurpose.HTTPS, StoreName.Root, StoreLocation.CurrentUser, TestCertificateSubject);
            }

            // Act
            DateTimeOffset now = DateTimeOffset.UtcNow;
            now = new DateTimeOffset(now.Year, now.Month, now.Day, now.Hour, now.Minute, now.Second, 0, now.Offset);
            var result = manager.EnsureAspNetCoreHttpsDevelopmentCertificate(now, now.AddYears(1), CertificateName, trust: false, subject: TestCertificateSubject);

            // Assert
            Assert.Equal(EnsureCertificateResult.Succeeded, result);
            Assert.True(File.Exists(CertificateName));

            var exportedCertificate = new X509Certificate2(File.ReadAllBytes(CertificateName));
            Assert.NotNull(exportedCertificate);
            Assert.False(exportedCertificate.HasPrivateKey);

            var httpsCertificates = manager.ListCertificates(CertificatePurpose.HTTPS, StoreName.My, StoreLocation.CurrentUser, isValid: false);
            var httpsCertificate = Assert.Single(httpsCertificates, c => c.Subject == TestCertificateSubject);
            Assert.True(httpsCertificate.HasPrivateKey);
            Assert.Equal(TestCertificateSubject, httpsCertificate.Subject);
            Assert.Equal(TestCertificateSubject, httpsCertificate.Issuer);
            Assert.Equal("sha256RSA", httpsCertificate.SignatureAlgorithm.FriendlyName);
            Assert.Equal("1.2.840.113549.1.1.11", httpsCertificate.SignatureAlgorithm.Value);

            Assert.Equal(now.LocalDateTime, httpsCertificate.NotBefore);
            Assert.Equal(now.AddYears(1).LocalDateTime, httpsCertificate.NotAfter);
            Assert.Contains(
                httpsCertificate.Extensions.OfType<X509Extension>(),
                e => e is X509BasicConstraintsExtension basicConstraints &&
                    basicConstraints.Critical == true &&
                    basicConstraints.CertificateAuthority == false &&
                    basicConstraints.HasPathLengthConstraint == false &&
                    basicConstraints.PathLengthConstraint == 0);

            Assert.Contains(
                httpsCertificate.Extensions.OfType<X509Extension>(),
                e => e is X509KeyUsageExtension keyUsage &&
                    keyUsage.Critical == true &&
                    keyUsage.KeyUsages == X509KeyUsageFlags.KeyEncipherment);

            Assert.Contains(
                httpsCertificate.Extensions.OfType<X509Extension>(),
                e => e is X509EnhancedKeyUsageExtension enhancedKeyUsage &&
                    enhancedKeyUsage.Critical == true &&
                    enhancedKeyUsage.EnhancedKeyUsages.OfType<Oid>().Single() is Oid keyUsage &&
                    keyUsage.Value == "1.3.6.1.5.5.7.3.1");

            // Subject alternative name
            Assert.Contains(
                httpsCertificate.Extensions.OfType<X509Extension>(),
                e => e.Critical == true &&
                    e.Oid.Value == "2.5.29.17");

            // ASP.NET HTTPS Development certificate extension
            Assert.Contains(
                httpsCertificate.Extensions.OfType<X509Extension>(),
                e => e.Critical == false &&
                    e.Oid.Value == "1.3.6.1.4.1.311.84.1.1" &&
                    Encoding.ASCII.GetString(e.RawData) == "ASP.NET Core HTTPS development certificate");

            Assert.Equal(httpsCertificate.GetCertHashString(), exportedCertificate.GetCertHashString());
        }

        [Fact]
        public void EnsureCreateHttpsCertificate_DoesNotCreateACertificate_WhenThereIsAnExistingHttpsCertificates()
        {
            // Arrange
            const string CertificateName = nameof(EnsureCreateHttpsCertificate_DoesNotCreateACertificate_WhenThereIsAnExistingHttpsCertificates) + ".pfx";
            var certificatePassword = Guid.NewGuid().ToString();

            var manager = new CertificateManager();

            manager.RemoveAllCertificates(CertificatePurpose.HTTPS, StoreName.My, StoreLocation.CurrentUser, TestCertificateSubject);
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                manager.RemoveAllCertificates(CertificatePurpose.HTTPS, StoreName.Root, StoreLocation.CurrentUser, TestCertificateSubject);
            }

            DateTimeOffset now = DateTimeOffset.UtcNow;
            now = new DateTimeOffset(now.Year, now.Month, now.Day, now.Hour, now.Minute, now.Second, 0, now.Offset);
            manager.EnsureAspNetCoreHttpsDevelopmentCertificate(now, now.AddYears(1), path: null, trust: false, subject: TestCertificateSubject);

            var httpsCertificate = manager.ListCertificates(CertificatePurpose.HTTPS, StoreName.My, StoreLocation.CurrentUser, isValid: false).Single(c => c.Subject == TestCertificateSubject);

            // Act
            var result = manager.EnsureAspNetCoreHttpsDevelopmentCertificate(now, now.AddYears(1), CertificateName, trust: false, includePrivateKey: true, password: certificatePassword, subject: TestCertificateSubject);

            // Assert
            Assert.Equal(EnsureCertificateResult.ValidCertificatePresent, result);
            Assert.True(File.Exists(CertificateName));

            var exportedCertificate = new X509Certificate2(File.ReadAllBytes(CertificateName), certificatePassword);
            Assert.NotNull(exportedCertificate);
            Assert.True(exportedCertificate.HasPrivateKey);


            Assert.Equal(httpsCertificate.GetCertHashString(), exportedCertificate.GetCertHashString());
        }

        [Fact]
        public void EnsureCreateIdentityTokenSigningCertificate_CreatesACertificate_WhenThereAreNoHttpsCertificates()
        {
            // Arrange
            const string CertificateName = nameof(EnsureCreateIdentityTokenSigningCertificate_CreatesACertificate_WhenThereAreNoHttpsCertificates) + ".cer";
            var manager = new CertificateManager();

            manager.RemoveAllCertificates(CertificatePurpose.Signing, StoreName.My, StoreLocation.CurrentUser, TestCertificateSubject);
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                manager.RemoveAllCertificates(CertificatePurpose.Signing, StoreName.Root, StoreLocation.CurrentUser, TestCertificateSubject);
            }

            // Act
            DateTimeOffset now = DateTimeOffset.UtcNow;
            now = new DateTimeOffset(now.Year, now.Month, now.Day, now.Hour, now.Minute, now.Second, 0, now.Offset);
            var result = manager.EnsureAspNetCoreApplicationTokensDevelopmentCertificate(now, now.AddYears(1), CertificateName, trust: false, subject: TestCertificateSubject);

            // Assert
            Assert.Equal(EnsureCertificateResult.Succeeded, result);
            Assert.True(File.Exists(CertificateName));

            var exportedCertificate = new X509Certificate2(File.ReadAllBytes(CertificateName));
            Assert.NotNull(exportedCertificate);
            Assert.False(exportedCertificate.HasPrivateKey);

            var identityCertificates = manager.ListCertificates(CertificatePurpose.Signing, StoreName.My, StoreLocation.CurrentUser, isValid: false);
            var identityCertificate = Assert.Single(identityCertificates, i => i.Subject == TestCertificateSubject);
            Assert.True(identityCertificate.HasPrivateKey);
            Assert.Equal(TestCertificateSubject, identityCertificate.Subject);
            Assert.Equal(TestCertificateSubject, identityCertificate.Issuer);
            Assert.Equal("sha256RSA", identityCertificate.SignatureAlgorithm.FriendlyName);
            Assert.Equal("1.2.840.113549.1.1.11", identityCertificate.SignatureAlgorithm.Value);

            Assert.Equal(now.LocalDateTime, identityCertificate.NotBefore);
            Assert.Equal(now.AddYears(1).LocalDateTime, identityCertificate.NotAfter);
            Assert.Contains(
                identityCertificate.Extensions.OfType<X509Extension>(),
                e => e is X509BasicConstraintsExtension basicConstraints &&
                    basicConstraints.Critical == true &&
                    basicConstraints.CertificateAuthority == false &&
                    basicConstraints.HasPathLengthConstraint == false &&
                    basicConstraints.PathLengthConstraint == 0);

            Assert.Contains(
                identityCertificate.Extensions.OfType<X509Extension>(),
                e => e is X509KeyUsageExtension keyUsage &&
                    keyUsage.Critical == true &&
                    keyUsage.KeyUsages == X509KeyUsageFlags.DigitalSignature);

            Assert.Contains(
                identityCertificate.Extensions.OfType<X509Extension>(),
                e => e is X509EnhancedKeyUsageExtension enhancedKeyUsage &&
                    enhancedKeyUsage.Critical == true &&
                    enhancedKeyUsage.EnhancedKeyUsages.OfType<Oid>().Single() is Oid keyUsage &&
                    keyUsage.Value == "1.3.6.1.5.5.7.3.1");

            // ASP.NET Core Identity Json Web Token signing development certificate
            Assert.Contains(
                identityCertificate.Extensions.OfType<X509Extension>(),
                e => e.Critical == false &&
                    e.Oid.Value == "1.3.6.1.4.1.311.84.1.2" &&
                    Encoding.ASCII.GetString(e.RawData) == "ASP.NET Core Identity Json Web Token signing development certificate");

            Assert.Equal(identityCertificate.GetCertHashString(), exportedCertificate.GetCertHashString());
        }

        [Fact]
        public void EnsureCreateIdentityTokenSigningCertificate_DoesNotCreateACertificate_WhenThereIsAnExistingHttpsCertificates()
        {
            // Arrange
            const string CertificateName = nameof(EnsureCreateIdentityTokenSigningCertificate_DoesNotCreateACertificate_WhenThereIsAnExistingHttpsCertificates) + ".pfx";
            var certificatePassword = Guid.NewGuid().ToString();

            var manager = new CertificateManager();

            manager.RemoveAllCertificates(CertificatePurpose.Signing, StoreName.My, StoreLocation.CurrentUser, TestCertificateSubject);
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                manager.RemoveAllCertificates(CertificatePurpose.Signing, StoreName.Root, StoreLocation.CurrentUser, TestCertificateSubject);
            }

            DateTimeOffset now = DateTimeOffset.UtcNow;
            now = new DateTimeOffset(now.Year, now.Month, now.Day, now.Hour, now.Minute, now.Second, 0, now.Offset);
            manager.EnsureAspNetCoreApplicationTokensDevelopmentCertificate(now, now.AddYears(1), path: null, trust: false, subject: TestCertificateSubject);

            var identityTokenSigningCertificates = manager.ListCertificates(CertificatePurpose.Signing, StoreName.My, StoreLocation.CurrentUser, isValid: false).Single(c => c.Subject == TestCertificateSubject);

            // Act
            var result = manager.EnsureAspNetCoreApplicationTokensDevelopmentCertificate(now, now.AddYears(1), CertificateName, trust: false, includePrivateKey: true, password: certificatePassword, subject: TestCertificateSubject);

            // Assert
            Assert.Equal(EnsureCertificateResult.ValidCertificatePresent, result);
            Assert.True(File.Exists(CertificateName));

            var exportedCertificate = new X509Certificate2(File.ReadAllBytes(CertificateName), certificatePassword);
            Assert.NotNull(exportedCertificate);
            Assert.True(exportedCertificate.HasPrivateKey);

            Assert.Equal(identityTokenSigningCertificates.GetCertHashString(), exportedCertificate.GetCertHashString());
        }
    }
}

#endif