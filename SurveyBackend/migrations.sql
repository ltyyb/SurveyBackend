CREATE TABLE IF NOT EXISTS `__EFMigrationsHistory` (
    `MigrationId` varchar(150) NOT NULL,
    `ProductVersion` varchar(32) NOT NULL,
    PRIMARY KEY (`MigrationId`)
);

START TRANSACTION;
CREATE TABLE `questionnaires` (
    `QuestionnaireId` varchar(8) NOT NULL,
    `FriendlyName` varchar(100) NOT NULL,
    `UniquePerUser` tinyint(1) NOT NULL,
    `NeedReview` tinyint(1) NOT NULL,
    `IsVerifyQuestionnaire` tinyint(1) NOT NULL,
    `ReleaseDate` datetime(6) NOT NULL,
    `SurveyJson` longtext NOT NULL,
    PRIMARY KEY (`QuestionnaireId`)
);

CREATE TABLE `users` (
    `UserId` varchar(16) NOT NULL,
    `QQId` varchar(16) NOT NULL,
    `UserGroup` int NOT NULL,
    PRIMARY KEY (`UserId`)
);

CREATE TABLE `requests` (
    `RequestId` varchar(16) NOT NULL,
    `RequestType` int NOT NULL,
    `UserId` varchar(16) NOT NULL,
    `IsDisabled` tinyint(1) NOT NULL,
    `CreatedAt` datetime(6) NOT NULL,
    PRIMARY KEY (`RequestId`),
    CONSTRAINT `FK_requests_users_UserId` FOREIGN KEY (`UserId`) REFERENCES `users` (`UserId`) ON DELETE RESTRICT
);

CREATE TABLE `submissions` (
    `SubmissionId` varchar(16) NOT NULL,
    `QuestionnaireId` varchar(8) NOT NULL,
    `UserId` varchar(16) NOT NULL,
    `CreatedAt` datetime(6) NOT NULL,
    `IsDisabled` tinyint(1) NOT NULL,
    `SurveyData` longtext NOT NULL,
    PRIMARY KEY (`SubmissionId`),
    CONSTRAINT `FK_submissions_questionnaires_QuestionnaireId` FOREIGN KEY (`QuestionnaireId`) REFERENCES `questionnaires` (`QuestionnaireId`) ON DELETE RESTRICT,
    CONSTRAINT `FK_submissions_users_UserId` FOREIGN KEY (`UserId`) REFERENCES `users` (`UserId`) ON DELETE RESTRICT
);

CREATE TABLE `review_submissions` (
    `ReviewSubmissionDataId` varchar(16) NOT NULL,
    `SubmissionId` varchar(16) NOT NULL,
    `AIInsights` longtext NOT NULL,
    `Status` int NOT NULL,
    PRIMARY KEY (`ReviewSubmissionDataId`),
    CONSTRAINT `FK_review_submissions_submissions_SubmissionId` FOREIGN KEY (`SubmissionId`) REFERENCES `submissions` (`SubmissionId`) ON DELETE RESTRICT
);

CREATE TABLE `review_votes` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `ReviewSubmissionDataId` varchar(16) NOT NULL,
    `UserId` varchar(16) NOT NULL,
    `VoteType` int NOT NULL,
    `VoteTime` datetime(6) NOT NULL,
    PRIMARY KEY (`Id`),
    CONSTRAINT `FK_review_votes_review_submissions_ReviewSubmissionDataId` FOREIGN KEY (`ReviewSubmissionDataId`) REFERENCES `review_submissions` (`ReviewSubmissionDataId`) ON DELETE RESTRICT,
    CONSTRAINT `FK_review_votes_users_UserId` FOREIGN KEY (`UserId`) REFERENCES `users` (`UserId`) ON DELETE RESTRICT
);

CREATE INDEX `IX_requests_UserId` ON `requests` (`UserId`);

CREATE INDEX `IX_review_submissions_SubmissionId` ON `review_submissions` (`SubmissionId`);

CREATE INDEX `IX_review_votes_ReviewSubmissionDataId` ON `review_votes` (`ReviewSubmissionDataId`);

CREATE INDEX `IX_review_votes_UserId` ON `review_votes` (`UserId`);

CREATE INDEX `IX_submissions_QuestionnaireId` ON `submissions` (`QuestionnaireId`);

CREATE INDEX `IX_submissions_UserId` ON `submissions` (`UserId`);

CREATE UNIQUE INDEX `IX_users_QQId` ON `users` (`QQId`);

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260206170122_InitialCreate', '10.0.2');

ALTER TABLE `questionnaires` DROP COLUMN `FriendlyName`;

ALTER TABLE `questionnaires` DROP COLUMN `IsVerifyQuestionnaire`;

ALTER TABLE `questionnaires` DROP COLUMN `NeedReview`;

ALTER TABLE `questionnaires` DROP COLUMN `UniquePerUser`;

ALTER TABLE `questionnaires` ADD `SurveyId` varchar(8) NOT NULL DEFAULT '';

CREATE TABLE `surveys` (
    `SurveyId` varchar(8) NOT NULL,
    `Title` varchar(200) NOT NULL,
    `Description` varchar(1000) NOT NULL,
    `UniquePerUser` tinyint(1) NOT NULL,
    `NeedReview` tinyint(1) NOT NULL,
    `IsVerifySurvey` tinyint(1) NOT NULL,
    `CreatedAt` datetime(6) NOT NULL,
    PRIMARY KEY (`SurveyId`)
);

CREATE INDEX `IX_questionnaires_SurveyId` ON `questionnaires` (`SurveyId`);

ALTER TABLE `questionnaires` ADD CONSTRAINT `FK_questionnaires_surveys_SurveyId` FOREIGN KEY (`SurveyId`) REFERENCES `surveys` (`SurveyId`) ON DELETE RESTRICT;

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260212102856_AddSurveyStructure', '10.0.2');

COMMIT;

