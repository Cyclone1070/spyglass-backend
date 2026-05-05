using System;
using spyglass_backend.Features.Links;
using Xunit;

namespace spyglass_backend.Tests
{
    public class ScraperBackgroundServiceTests
    {
        [Fact]
        public void GetNextRunTime_WhenFriday_ReturnsThisSundayMidnight()
        {
            // Arrange
            var friday = new DateTime(2026, 5, 1); // Friday May 1st 2026

            // Act
            var nextRun = ScraperBackgroundService.CalculateNextRunTime(friday);

            // Assert
            Assert.Equal(DayOfWeek.Sunday, nextRun.DayOfWeek);
            Assert.Equal(2026, nextRun.Year);
            Assert.Equal(5, nextRun.Month);
            Assert.Equal(3, nextRun.Day); // Sunday May 3rd
            Assert.Equal(0, nextRun.Hour);
        }

        [Fact]
        public void GetNextRunTime_WhenSundayMorning_ReturnsNextSundayMidnight()
        {
            // Arrange
            var sundayMorning = new DateTime(2026, 5, 3, 0, 0, 1); // Sunday May 3rd 2026, 00:00:01

            // Act
            var nextRun = ScraperBackgroundService.CalculateNextRunTime(sundayMorning);

            // Assert
            Assert.Equal(DayOfWeek.Sunday, nextRun.DayOfWeek);
            Assert.Equal(2026, nextRun.Year);
            Assert.Equal(5, nextRun.Month);
            Assert.Equal(10, nextRun.Day); // Sunday May 10th
            Assert.Equal(0, nextRun.Hour);
        }

        [Fact]
        public void GetNextRunTime_WhenLeapYearFeb28_ReturnsNextSunday()
        {
            // Arrange
            var leapYearFeb28 = new DateTime(2024, 2, 28); // Wednesday Feb 28th 2024
            
            // Act
            var nextRun = ScraperBackgroundService.CalculateNextRunTime(leapYearFeb28);
            
            // Assert
            Assert.Equal(DayOfWeek.Sunday, nextRun.DayOfWeek);
            Assert.Equal(2024, nextRun.Year);
            Assert.Equal(3, nextRun.Month);
            Assert.Equal(3, nextRun.Day); // Sunday March 3rd (accounting for Feb 29th)
            Assert.Equal(0, nextRun.Hour);
        }

        [Fact]
        public void GetNextRunTime_WhenNewYearsEve_ReturnsNextSunday()
        {
            // Arrange
            var nye = new DateTime(2025, 12, 31); // Wednesday Dec 31st 2025
            
            // Act
            var nextRun = ScraperBackgroundService.CalculateNextRunTime(nye);
            
            // Assert
            Assert.Equal(DayOfWeek.Sunday, nextRun.DayOfWeek);
            Assert.Equal(2026, nextRun.Year);
            Assert.Equal(1, nextRun.Month);
            Assert.Equal(4, nextRun.Day); // Sunday Jan 4th 2026
            Assert.Equal(0, nextRun.Hour);
        }
    }
}
