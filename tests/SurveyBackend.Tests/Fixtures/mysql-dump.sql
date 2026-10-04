SET FOREIGN_KEY_CHECKS = 0;
CREATE TABLE `__efmigrationshistory` (
  `MigrationId` varchar(150) NOT NULL,
  `ProductVersion` varchar(32) NOT NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
CREATE TABLE `users` (
  `UserId` varchar(16) NOT NULL,
  `QQId` varchar(16) NOT NULL,
  `UserGroup` int NOT NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
CREATE TABLE `surveys` (
  `SurveyId` varchar(8) NOT NULL,
  `Title` varchar(200) NOT NULL,
  `Description` varchar(1000) NOT NULL,
  `UniquePerUser` tinyint(1) NOT NULL,
  `NeedReview` tinyint(1) NOT NULL,
  `IsVerifySurvey` tinyint(1) NOT NULL,
  `CreatedAt` datetime(6) NOT NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
CREATE TABLE `questionnaires` (
  `QuestionnaireId` varchar(8) NOT NULL,
  `SurveyId` varchar(8) NOT NULL,
  `LLMPageNames` json DEFAULT NULL,
  `ReleaseDate` datetime(6) NOT NULL,
  `SurveyJson` longtext NOT NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
CREATE TABLE `submissions` (
  `SubmissionId` varchar(16) NOT NULL,
  `QuestionnaireId` varchar(8) NOT NULL,
  `UserId` varchar(16) NOT NULL,
  `CreatedAt` datetime(6) NOT NULL,
  `IsDisabled` tinyint(1) NOT NULL,
  `SurveyData` longtext NOT NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
CREATE TABLE `review_submissions` (
  `ReviewSubmissionDataId` varchar(16) NOT NULL,
  `SubmissionId` varchar(16) NOT NULL,
  `AIInsights` longtext NOT NULL,
  `Status` int NOT NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
CREATE TABLE `review_votes` (
  `Id` int NOT NULL AUTO_INCREMENT,
  `ReviewSubmissionDataId` varchar(16) NOT NULL,
  `UserId` varchar(16) NOT NULL,
  `VoteType` int NOT NULL,
  `VoteTime` datetime(6) NOT NULL
) ENGINE=InnoDB AUTO_INCREMENT=100 DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
CREATE TABLE `requests` (
  `RequestId` varchar(16) NOT NULL,
  `RequestType` int NOT NULL,
  `UserId` varchar(16) NOT NULL,
  `IsDisabled` tinyint(1) NOT NULL,
  `CreatedAt` datetime(6) NOT NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
SET FOREIGN_KEY_CHECKS = 1;
INSERT INTO `__efmigrationshistory` (`MigrationId`,`ProductVersion`) VALUES ('20260215210633_SetCascadeDeleteBehavior','10.0.3');
INSERT INTO `review_votes` (`Id`,`ReviewSubmissionDataId`,`UserId`,`VoteType`,`VoteTime`) VALUES ('37','ReviewMixedCase1','ReviewerCase001','1','2026-10-01 00:00:00.000001');
INSERT INTO `review_submissions` (`ReviewSubmissionDataId`,`SubmissionId`,`AIInsights`,`Status`) VALUES ('ReviewMixedCase1','SubmitMixedCase1','中文😀;逗号,单引号\'和双引号\"及反斜杠\\
第二行','0');
INSERT INTO `submissions` (`SubmissionId`,`QuestionnaireId`,`UserId`,`CreatedAt`,`IsDisabled`,`SurveyData`) VALUES ('SubmitMixedCase1','FormAb01','UserMixedCase01','2026-10-01 00:00:00.000000','0','{"answer":"中文😀; \\"quoted\\"","score":2}');
INSERT INTO `questionnaires` (`QuestionnaireId`,`SurveyId`,`LLMPageNames`,`ReleaseDate`,`SurveyJson`) VALUES ('FormAb01','Survey01','["页面一"]','2026-10-01 00:00:00.000001','{"pages":[{"name":"页面一","elements":[{"type":"text","name":"answer"}]}]}'),('FormAb02','Survey01',NULL,'2026-10-01 00:00:00.000000','{}');
INSERT INTO `surveys` (`SurveyId`,`Title`,`Description`,`UniquePerUser`,`NeedReview`,`IsVerifySurvey`,`CreatedAt`) VALUES ('Survey01','测试问卷','', '1','1','1','2026-10-01 00:00:00.000000');
INSERT INTO `users` (`UserId`,`QQId`,`UserGroup`) VALUES ('UserMixedCase01','123456','1'),('ReviewerCase001','654321','2'),('NewUserCase001','111111','0');
INSERT INTO `requests` (`RequestId`,`RequestType`,`UserId`,`IsDisabled`,`CreatedAt`) VALUES ('RequestMixed001','0','UserMixedCase01','0','2026-10-01 00:00:00.000000');
